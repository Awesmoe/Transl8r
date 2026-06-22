"""Routes translated text to the enabled output sinks: overlay, TTS, file."""

import threading
import time
from pathlib import Path


class TTSEngine:
    """kokoro-onnx wrapper. Model files are user-supplied (see README)."""

    def __init__(self, cfg: dict):
        from kokoro_onnx import Kokoro  # raises if not installed
        model = cfg.get("tts_model_path", "kokoro-v1.0.onnx")
        voices = cfg.get("tts_voices_path", "voices-v1.0.bin")
        if not (Path(model).exists() and Path(voices).exists()):
            raise FileNotFoundError(
                f"TTS model files not found: {model} / {voices}")
        self.kokoro = Kokoro(model, voices)
        self.voice = cfg.get("tts_voice", "af_sarah")
        self._lock = threading.Lock()

    def speak(self, text: str):
        def _run():
            with self._lock:  # don't overlap utterances
                try:
                    import sounddevice as sd
                    samples, sr = self.kokoro.create(text, voice=self.voice)
                    sd.play(samples, sr)
                    sd.wait()
                except Exception as e:
                    print(f"[tts] error: {e}")
        threading.Thread(target=_run, daemon=True).start()


class OutputRouter:
    def __init__(self, cfg: dict):
        self.tts = None
        self.apply_config(cfg)

    def apply_config(self, cfg: dict):
        self.cfg = cfg
        if cfg.get("output_tts") and self.tts is None:
            try:
                self.tts = TTSEngine(cfg)
            except Exception as e:
                print(f"[tts] disabled: {e}")
                self.tts = None
        elif not cfg.get("output_tts"):
            self.tts = None

    def handle(self, original: str, translated: str, overlay=None):
        if self.cfg.get("output_overlay") and overlay:
            overlay.show_text(original, translated)
        if self.cfg.get("output_tts") and self.tts:
            self.tts.speak(translated)
        if self.cfg.get("output_file"):
            try:
                path = Path(self.cfg.get("output_file_path", "transl8r_log.txt"))
                stamp = time.strftime("%H:%M:%S")
                line = f"[{stamp}] {original + ' => ' if original else ''}{translated}\n"
                with path.open("a", encoding="utf-8") as f:
                    f.write(line)
            except OSError as e:
                print(f"[file] write error: {e}")
        # console always, it's free
        print(f">> {original + '  =>  ' if original else ''}{translated}")
