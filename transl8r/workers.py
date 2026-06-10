"""Worker threads for the two input pipelines.

Both emit text_ready(original, translated) and error(str).
Heavy ML imports happen inside run() so the GUI starts instantly.
"""

import time

import numpy as np
from PySide6.QtCore import QThread, Signal


class OcrWorker(QThread):
    text_ready = Signal(int, str, str)   # region index, original, translated
    text_cleared = Signal(int)           # region index: text left the screen
    error = Signal(str)
    status = Signal(str)

    def __init__(self, cfg: dict, parent=None):
        super().__init__(parent)
        self.cfg = cfg
        self._stop = False

    def stop(self):
        self._stop = True

    def run(self):
        regions = self.cfg.get("regions") or []
        if not regions:
            self.error.emit("No screen region selected.")
            return
        backend_name = self.cfg.get("ocr_backend", "manga-ocr")
        try:
            self.status.emit(f"Loading OCR backend '{backend_name}'...")
            import mss
            from PIL import Image
            from .ocr_backends import build_ocr_backend, looks_japanese
            from .translate import build_translator
            ocr = build_ocr_backend(self.cfg)
            direct = hasattr(ocr, "recognize_translate")
            translator = None if direct else build_translator(self.cfg)
        except Exception as e:
            self.error.emit(f"OCR pipeline init failed: {e}")
            return

        self.status.emit(
            f"OCR running ({backend_name}, {len(regions)} region"
            f"{'s' if len(regions) > 1 else ''})")
        poll = float(self.cfg.get("poll_interval", 0.5))
        change_ratio = float(self.cfg.get("frame_change_ratio", 0.01))
        states = [{"prev": None, "processed": None,
                   "last_text": None, "unstable": 0} for _ in regions]

        def changed(a, b) -> bool:
            """True if frames differ in more than `change_ratio` of pixels.
            Tolerant comparison so a blinking 'next' arrow or subtle textbox
            shimmer doesn't count as a change, but new dialogue does."""
            if a is None or b is None or a.shape != b.shape:
                return True
            diff = np.abs(a.astype(np.int16) - b.astype(np.int16)) > 24
            return float(diff.mean()) > change_ratio

        with mss.mss() as sct:
            while not self._stop:
                for i, region in enumerate(regions):
                    if self._stop:
                        break
                    st = states[i]
                    try:
                        shot = sct.grab(region)
                        cur = np.frombuffer(shot.rgb, dtype=np.uint8).copy()

                        stable = not changed(cur, st["prev"])
                        st["prev"] = cur
                        st["unstable"] = 0 if stable else st["unstable"] + 1

                        # OCR when the frame settled — or force after 5
                        # restless polls (perpetually animated textboxes)
                        if not ((stable or st["unstable"] >= 5)
                                and changed(cur, st["processed"])):
                            continue
                        st["unstable"] = 0
                        st["processed"] = cur
                        img = Image.frombytes("RGB", shot.size, shot.rgb)
                        if direct:
                            text, translated = ocr.recognize_translate(img)
                        else:
                            text = ocr.recognize(img)
                            translated = None

                        if not text:
                            # dialogue gone: clear that region's overlay and
                            # forget last_text so a repeat line shows again
                            if st["last_text"]:
                                st["last_text"] = None
                                self.text_cleared.emit(i)
                            continue
                        if text != st["last_text"] and looks_japanese(text):
                            st["last_text"] = text
                            if translated is None:
                                translated = translator.translate(text)
                            self.text_ready.emit(i, text, translated)
                    except Exception as e:
                        self.error.emit(f"OCR loop error: {e}")
                        time.sleep(2)
                time.sleep(poll)


class AudioWorker(QThread):
    """WASAPI loopback -> faster-whisper.

    Needs sounddevice >= 0.5 on Windows, which exposes loopback devices as
    extra inputs named "<output device> [Loopback]".
    """

    text_ready = Signal(str, str)
    error = Signal(str)
    status = Signal(str)

    SILENCE_RMS = 0.01      # below this = silence
    MIN_CHUNK_S = 2.0       # don't transcribe less than this
    MAX_CHUNK_S = 8.0       # force flush at this length
    TAIL_SILENCE_S = 0.6    # flush when this much trailing silence

    def __init__(self, cfg: dict, parent=None):
        super().__init__(parent)
        self.cfg = cfg
        self._stop = False
        self._cap_sr = 48000
        self._cap_name = "?"

    def stop(self):
        self._stop = True

    @staticmethod
    def _find_loopback_device(sd):
        devices = sd.query_devices()
        try:
            default_out = sd.query_devices(kind="output")["name"]
        except Exception:
            default_out = ""
        candidates = [
            (i, d) for i, d in enumerate(devices)
            if "[Loopback]" in d["name"] and d["max_input_channels"] > 0
        ]
        if not candidates:
            return None, None
        for i, d in candidates:
            if default_out and default_out in d["name"]:
                return i, d
        return candidates[0]

    # -------------------------------------------------- capture backends
    # Each generator yields mono float32 blocks (~0.25s) and sets
    # self._cap_sr / self._cap_name before the first yield.

    def _sd_blocks(self):
        """sounddevice, if the PortAudio build exposes [Loopback] devices."""
        import sounddevice as sd
        idx, dev = self._find_loopback_device(sd)
        if idx is None:
            return None
        sr = int(dev["default_samplerate"]) or 48000
        channels = min(2, dev["max_input_channels"])
        self._cap_sr, self._cap_name = sr, dev["name"]

        def gen():
            block = int(sr * 0.25)
            with sd.InputStream(device=idx, channels=channels,
                                samplerate=sr, blocksize=block) as stream:
                while not self._stop:
                    data, _overflow = stream.read(block)
                    mono = data.mean(axis=1) if data.ndim > 1 else data
                    yield mono.astype(np.float32)
        return gen()

    def _soundcard_blocks(self):
        """soundcard library: WASAPI loopback via its own bindings, no
        PortAudio involved. Imported inside this thread so COM init lands
        on the right thread."""
        import soundcard as sc
        spk = sc.default_speaker()
        mic = None
        try:
            mic = sc.get_microphone(spk.name, include_loopback=True)
        except Exception:
            pass
        if mic is None:
            loops = [m for m in sc.all_microphones(include_loopback=True)
                     if getattr(m, "isloopback", False)]
            if not loops:
                return None
            mic = loops[0]
        sr = 48000
        self._cap_sr, self._cap_name = sr, f"{mic.name} (soundcard)"

        def gen():
            block = int(sr * 0.25)
            with mic.recorder(samplerate=sr, channels=2) as rec:
                while not self._stop:
                    data = rec.record(numframes=block)
                    mono = data.mean(axis=1) if data.ndim > 1 else data
                    yield mono.astype(np.float32)
        return gen()

    def _open_capture(self):
        try:
            blocks = self._sd_blocks()
            if blocks is not None:
                return blocks
        except Exception as e:
            print(f"[audio] sounddevice loopback unavailable: {e}")
        try:
            blocks = self._soundcard_blocks()
            if blocks is not None:
                return blocks
        except ImportError:
            self.error.emit(
                "No [Loopback] devices in your PortAudio build, and the "
                "'soundcard' fallback isn't installed. Fix: pip install "
                "soundcard")
            return None
        except Exception as e:
            self.error.emit(f"soundcard capture failed: {e}")
            return None
        self.error.emit("No system-audio loopback capture available.")
        return None

    def run(self):
        try:
            from faster_whisper import WhisperModel
        except Exception as e:
            self.error.emit(f"Audio deps missing: {e}")
            return

        try:
            self.status.emit(f"Loading whisper '{self.cfg.get('whisper_model')}'...")
            device = self.cfg.get("whisper_device", "auto")
            model = WhisperModel(self.cfg.get("whisper_model", "small"),
                                 device=device,
                                 compute_type="auto")
            translator = None
            if self.cfg.get("audio_use_translator"):
                from .translate import build_translator
                translator = build_translator(self.cfg)
        except Exception as e:
            self.error.emit(f"Whisper init failed: {e}")
            return

        blocks = self._open_capture()
        if blocks is None:
            return
        sr_in = self._cap_sr
        buf = []          # list of mono float32 blocks @ sr_in
        silent_tail = 0.0

        self.status.emit(f"Listening to: {self._cap_name}")
        try:
            for mono in blocks:
                if self._stop:
                    break
                buf.append(mono)
                buf_len = sum(len(b) for b in buf) / sr_in
                rms = float(np.sqrt(np.mean(mono ** 2))) if len(mono) else 0.0
                silent_tail = silent_tail + 0.25 if rms < self.SILENCE_RMS else 0.0

                flush = (buf_len >= self.MAX_CHUNK_S or
                         (buf_len >= self.MIN_CHUNK_S and
                          silent_tail >= self.TAIL_SILENCE_S))
                if not flush:
                    continue

                audio = np.concatenate(buf)
                buf.clear()
                silent_tail = 0.0

                if float(np.sqrt(np.mean(audio ** 2))) < self.SILENCE_RMS:
                    continue  # whole chunk is silence

                # resample to 16k mono for whisper
                n_out = int(len(audio) * 16000 / sr_in)
                audio16 = np.interp(
                    np.linspace(0, len(audio) - 1, n_out),
                    np.arange(len(audio)), audio).astype(np.float32)

                task = "transcribe" if translator else "translate"
                segments, _info = model.transcribe(
                    audio16, task=task, language="ja",
                    vad_filter=True, beam_size=2)
                text = " ".join(s.text.strip() for s in segments).strip()
                if not text:
                    continue
                if translator:
                    self.text_ready.emit(text, translator.translate(text))
                else:
                    self.text_ready.emit("", text)
        except Exception as e:
            self.error.emit(f"Audio loop error: {e}")
