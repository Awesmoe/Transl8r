"""transl8r tray app — wires pipelines, overlay, outputs, and settings together."""

import sys

from PySide6.QtCore import Qt
from PySide6.QtGui import QAction, QColor, QIcon, QPainter, QPixmap
from PySide6.QtWidgets import QApplication, QMenu, QMessageBox, QSystemTrayIcon

from . import config
from .hotkeys import GlobalHotkeys
from .outputs import OutputRouter
from .overlay import Overlay
from .region import RegionSelector
from .settings import SettingsDialog
from .workers import AudioWorker, OcrWorker


def _make_icon() -> QIcon:
    pm = QPixmap(64, 64)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setBrush(QColor(30, 30, 40))
    p.setPen(Qt.PenStyle.NoPen)
    p.drawRoundedRect(2, 2, 60, 60, 14, 14)
    p.setPen(QColor(120, 210, 255))
    font = p.font()
    font.setPixelSize(36)
    font.setBold(True)
    p.setFont(font)
    p.drawText(pm.rect(), Qt.AlignmentFlag.AlignCenter, "訳")
    p.end()
    return QIcon(pm)


class App:
    def __init__(self):
        self.qapp = QApplication(sys.argv)
        self.qapp.setQuitOnLastWindowClosed(False)

        self.cfg = config.load()
        self.edit_mode = False
        self.overlays: list[Overlay] = []
        self.audio_overlay = Overlay(
            self.cfg, region=None,
            offset=tuple(self.cfg.get("audio_overlay_offset", [0, 0])))
        self.audio_overlay.moved.connect(self._save_audio_offset)
        self._rebuild_overlays()
        self.router = OutputRouter(self.cfg)
        self.ocr_worker: OcrWorker | None = None
        self.audio_worker: AudioWorker | None = None
        self.selector = None
        self._zombies: list = []

        self._build_tray()
        self.hotkeys = GlobalHotkeys(self.qapp)
        self.hotkeys.triggered.connect(self._on_hotkey)
        self._apply_hotkeys()
        self._sync_workers()

    # ---------------------------------------------------------------- tray

    def _build_tray(self):
        self.tray = QSystemTrayIcon(_make_icon())
        menu = QMenu()

        self.act_ocr = QAction("Screen OCR", checkable=True)
        self.act_ocr.setChecked(self.cfg["ocr_enabled"])  # connect AFTER set
        self.act_ocr.toggled.connect(self._toggle_ocr)
        menu.addAction(self.act_ocr)

        self.act_audio = QAction("Audio translation", checkable=True)
        self.act_audio.setChecked(self.cfg["audio_enabled"])  # connect AFTER set
        self.act_audio.toggled.connect(self._toggle_audio)
        menu.addAction(self.act_audio)

        menu.addSeparator()

        act_region = QAction("Select screen region...")
        act_region.triggered.connect(lambda: self.pick_region())
        menu.addAction(act_region)

        act_add_region = QAction("Add screen region")
        act_add_region.triggered.connect(lambda: self.pick_region(add=True))
        menu.addAction(act_add_region)

        self.act_overlay = QAction("Show overlay", checkable=True)
        self.act_overlay.setChecked(self.cfg["output_overlay"])
        self.act_overlay.toggled.connect(self._toggle_overlay)
        menu.addAction(self.act_overlay)

        self.act_edit = QAction("Edit overlay positions", checkable=True)
        self.act_edit.toggled.connect(self._toggle_edit_mode)
        menu.addAction(self.act_edit)

        act_settings = QAction("Settings...")
        act_settings.triggered.connect(self.open_settings)
        menu.addAction(act_settings)

        menu.addSeparator()
        act_quit = QAction("Quit")
        act_quit.triggered.connect(self.quit)
        menu.addAction(act_quit)

        # keep refs so Qt doesn't GC the actions
        self._menu = menu
        self._actions = [act_region, act_add_region, act_settings, act_quit]

        self.tray.setContextMenu(menu)
        self.tray.setToolTip("transl8r")
        self.tray.show()

    def _apply_hotkeys(self):
        bindings = {
            "region": self.cfg.get("hotkey_region", ""),
            "overlay": self.cfg.get("hotkey_overlay", ""),
            "edit": self.cfg.get("hotkey_edit", ""),
        }
        failed = self.hotkeys.set_bindings(bindings)
        for name, combo in bindings.items():
            if not combo:
                continue
            if combo in failed:
                print(f"[hotkey] FAILED: {name} = {combo!r} — already in use "
                      "by another app? Rebind it in Settings.")
            else:
                print(f"[hotkey] registered: {name} = {combo}")
        for combo in failed:
            self._notify("transl8r", f"Hotkey '{combo}' could not be "
                         "registered (in use by another app?)")

    def _on_hotkey(self, name: str):
        if name == "region":
            self.pick_region()
        elif name == "overlay":
            self.act_overlay.toggle()
        elif name == "edit":
            self.act_edit.toggle()

    def _notify(self, title: str, msg: str):
        self.tray.showMessage(title, msg, QSystemTrayIcon.MessageIcon.Information,
                              4000)

    # -------------------------------------------------------------- overlays

    def _rebuild_overlays(self):
        for ov in self.overlays:
            ov.clear()
            ov.deleteLater()
        offsets = self.cfg.get("overlay_offsets", [])
        self.overlays = []
        for i, r in enumerate(self.cfg.get("regions", [])):
            off = tuple(offsets[i]) if i < len(offsets) else (0, 0)
            ov = Overlay(self.cfg, region=r, offset=off)
            ov.moved.connect(lambda idx=i: self._save_overlay_offset(idx))
            self.overlays.append(ov)
        if self.edit_mode:  # picker/settings rebuilt while editing — re-enter
            for ov in self.overlays:
                ov.set_edit_mode(True)

    def _overlay_for(self, idx: int) -> "Overlay":
        if 0 <= idx < len(self.overlays):
            return self.overlays[idx]
        return self.audio_overlay

    def _all_overlays(self):
        return [*self.overlays, self.audio_overlay]

    # ------------------------------------------------------------- pipelines

    def _sync_workers(self):
        """Start/stop workers to match config."""
        # OCR
        if self.cfg["ocr_enabled"] and self.ocr_worker is None:
            if not self.cfg.get("regions"):
                self._notify("transl8r", "Pick a screen region first.")
                self.pick_region(then_start=True)
            else:
                self._start_ocr()
        elif not self.cfg["ocr_enabled"] and self.ocr_worker:
            self._stop_worker("ocr_worker")

        # Audio
        if self.cfg["audio_enabled"] and self.audio_worker is None:
            self.audio_worker = AudioWorker(self.cfg)
            self.audio_worker.text_ready.connect(self._on_audio_text)
            self._wire(self.audio_worker)
            self.audio_worker.start()
        elif not self.cfg["audio_enabled"] and self.audio_worker:
            self._stop_worker("audio_worker")

    def _start_ocr(self):
        self.ocr_worker = OcrWorker(self.cfg)
        self.ocr_worker.text_ready.connect(self._on_ocr_text)
        self.ocr_worker.text_cleared.connect(self._on_ocr_cleared)
        self._wire(self.ocr_worker)
        self.ocr_worker.start()

    def _wire(self, worker):
        worker.error.connect(self._on_error)
        worker.status.connect(self._on_status)

    def _on_ocr_text(self, idx: int, original: str, translated: str):
        self.router.handle(original, translated, self._overlay_for(idx))

    def _on_ocr_cleared(self, idx: int):
        self._overlay_for(idx).clear()

    def _on_audio_text(self, original: str, translated: str):
        self.router.handle(original, translated, self.audio_overlay)

    def _stop_worker(self, attr: str):
        worker = getattr(self, attr)
        if worker is None:
            return
        setattr(self, attr, None)
        worker.stop()
        if not worker.wait(1500):
            # still blocked (e.g. mid VLM request) — keep a reference until
            # it actually finishes, or Qt crashes with "QThread: Destroyed
            # while thread is still running"
            self._zombies.append(worker)
            worker.finished.connect(
                lambda w=worker: w in self._zombies and self._zombies.remove(w))

    def _on_status(self, msg: str):
        print(f"[status] {msg}")
        self.tray.setToolTip(f"transl8r — {msg}")

    def _on_error(self, msg: str):
        print(f"[error] {msg}")
        self._notify("transl8r error", msg)

    # --------------------------------------------------------------- actions

    def _toggle_ocr(self, on: bool):
        self.cfg["ocr_enabled"] = on
        config.save(self.cfg)
        self._sync_workers()

    def _toggle_audio(self, on: bool):
        self.cfg["audio_enabled"] = on
        config.save(self.cfg)
        self._sync_workers()

    def _toggle_overlay(self, on: bool):
        self.cfg["output_overlay"] = on
        config.save(self.cfg)
        self.router.apply_config(self.cfg)
        if on:
            # re-show boxes that still hold text (toggling on otherwise does
            # nothing until the next OCR change re-fires show_text)
            for ov in self._all_overlays():
                ov.show_if_text()
        else:
            for ov in self._all_overlays():
                ov.hide()

    def _edit_targets(self):
        """Overlays that participate in edit mode: screen regions always, plus
        the audio overlay only when audio is on (else it'd show a phantom
        bottom-center 'drag me' box for a pipeline that isn't running)."""
        targets = list(self.overlays)
        if self.cfg.get("audio_enabled"):
            targets.append(self.audio_overlay)
        return targets

    def _toggle_edit_mode(self, on: bool):
        self.edit_mode = on
        for ov in self._edit_targets():
            ov.set_edit_mode(on)
        if on:
            self._notify("transl8r", "Edit mode: drag overlays to reposition, "
                         "then toggle off to lock and re-enable click-through.")

    def _save_overlay_offset(self, idx: int):
        offs = self.cfg.setdefault("overlay_offsets", [])
        while len(offs) <= idx:
            offs.append([0, 0])
        offs[idx] = list(self.overlays[idx].offset)
        config.save(self.cfg)

    def _save_audio_offset(self):
        self.cfg["audio_overlay_offset"] = list(self.audio_overlay.offset)
        config.save(self.cfg)

    def pick_region(self, then_start: bool = False, add: bool = False):
        if self.edit_mode:  # don't carry edit mode into the picker
            self.act_edit.setChecked(False)
        # pause OCR so it doesn't capture the dimmed selector overlay
        was_running = self.ocr_worker is not None
        self._stop_worker("ocr_worker")
        for ov in self._all_overlays():
            ov.hide()

        if self.selector is not None and self.selector.isVisible():
            return
        self.selector = RegionSelector(self.cfg.get("regions", []))
        self.selector.region_selected.connect(
            lambda r: self._region_done(r, then_start or was_running, add))
        self.selector.cancelled.connect(
            lambda: self._region_cancelled(was_running))
        self.selector.showFullScreen()
        self.selector.raise_()
        self.selector.activateWindow()

    def _region_done(self, region: dict, start: bool, add: bool):
        if add:
            self.cfg.setdefault("regions", []).append(region)
        else:
            # fresh single region — drop offsets so the box isn't nudged by a
            # leftover offset from a previous region layout.
            self.cfg["regions"] = [region]
            self.cfg["overlay_offsets"] = []
        config.save(self.cfg)
        self._rebuild_overlays()
        n = len(self.cfg["regions"])
        self._notify("transl8r", f"Region saved ({n} active). "
                     "'Add screen region' in the tray appends more.")
        if start and self.cfg["ocr_enabled"]:
            self._start_ocr()

    def _region_cancelled(self, was_running: bool):
        if was_running and self.cfg["ocr_enabled"] and self.cfg.get("regions"):
            self._start_ocr()

    def open_settings(self):
        dlg = SettingsDialog(self.cfg)
        if dlg.exec():
            old = self.cfg
            self.cfg = dlg.result_config()
            config.save(self.cfg)
            # The settings dialog never edits regions, so rebuilding would only
            # blank the box currently on screen. Update existing overlays in
            # place (preserves shown text + dragged offset, applies display
            # options live); rebuild only if regions somehow differ.
            if old.get("regions") != self.cfg.get("regions"):
                self._rebuild_overlays()
            else:
                for ov in self.overlays:
                    ov.apply_config(self.cfg)
            self.audio_overlay.apply_config(self.cfg)
            self.router.apply_config(self.cfg)
            self._apply_hotkeys()
            for act, key in ((self.act_ocr, "ocr_enabled"),
                             (self.act_audio, "audio_enabled"),
                             (self.act_overlay, "output_overlay")):
                act.blockSignals(True)
                act.setChecked(self.cfg[key])
                act.blockSignals(False)

            # restart workers if their settings changed
            restart_keys_ocr = ("poll_interval", "frame_change_ratio",
                                "translator", "deepl_api_key",
                                "server_url", "server_model", "regions",
                                "ocr_backend", "paddle_min_confidence",
                                "vlm_url", "vlm_model")
            restart_keys_audio = ("whisper_model", "whisper_device",
                                  "audio_use_translator", "translator",
                                  "deepl_api_key", "server_url", "server_model")
            if any(old.get(k) != self.cfg.get(k) for k in restart_keys_ocr):
                self._stop_worker("ocr_worker")
            if any(old.get(k) != self.cfg.get(k) for k in restart_keys_audio):
                self._stop_worker("audio_worker")
            self._sync_workers()

    def quit(self):
        self.hotkeys.unregister_all()
        self._stop_worker("ocr_worker")
        self._stop_worker("audio_worker")
        for w in list(self._zombies):
            if not w.wait(4000):
                w.terminate()  # process is exiting anyway
                w.wait(1000)
        self.tray.hide()
        self.qapp.quit()

    # ------------------------------------------------------------------ run

    def run(self) -> int:
        if not QSystemTrayIcon.isSystemTrayAvailable():
            QMessageBox.critical(None, "transl8r", "No system tray available.")
            return 1
        return self.qapp.exec()


def main():
    sys.exit(App().run())


if __name__ == "__main__":
    main()
