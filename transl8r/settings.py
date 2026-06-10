"""Settings dialog. Edits a copy of the config; caller applies on accept."""

from PySide6.QtWidgets import (QCheckBox, QComboBox, QDialog, QDialogButtonBox,
                               QDoubleSpinBox, QFormLayout, QGroupBox,
                               QLineEdit, QSpinBox, QTabWidget, QVBoxLayout)

WHISPER_MODELS = ["tiny", "base", "small", "medium", "large-v3"]
TRANSLATORS = ["argos", "deepl", "server"]
OCR_BACKENDS = ["manga-ocr", "paddle", "vlm", "vlm-direct"]


class SettingsDialog(QDialog):
    def __init__(self, cfg: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("transl8r settings")
        self.cfg = dict(cfg)

        tabs = QTabWidget()
        tabs.addTab(self._input_tab(), "Input")
        tabs.addTab(self._translation_tab(), "Translation")
        tabs.addTab(self._output_tab(), "Output")

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok
                                   | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)

        root = QVBoxLayout(self)
        root.addWidget(tabs)
        root.addWidget(buttons)

    # ---------------------------------------------------------------- tabs

    def _input_tab(self):
        box = QGroupBox()
        form = QFormLayout(box)

        self.ocr_enabled = QCheckBox()
        self.ocr_enabled.setChecked(self.cfg["ocr_enabled"])
        form.addRow("Screen OCR pipeline", self.ocr_enabled)

        self.poll = QDoubleSpinBox()
        self.poll.setRange(0.1, 30.0)
        self.poll.setSingleStep(0.1)
        self.poll.setValue(self.cfg["poll_interval"])
        form.addRow("Poll interval (s)", self.poll)

        self.frame_change = QDoubleSpinBox()
        self.frame_change.setRange(0.0, 1.0)
        self.frame_change.setDecimals(3)
        self.frame_change.setSingleStep(0.005)
        self.frame_change.setValue(self.cfg["frame_change_ratio"])
        self.frame_change.setToolTip(
            "Fraction of pixels that must change before a frame is re-read.\n"
            "0.01 = 1%. Raise to ignore animated/shimmering textboxes; lower "
            "to catch small text changes.")
        form.addRow("Frame change threshold", self.frame_change)

        self.ocr_backend = QComboBox()
        self.ocr_backend.addItems(OCR_BACKENDS)
        self.ocr_backend.setCurrentText(self.cfg["ocr_backend"])
        form.addRow("OCR backend", self.ocr_backend)

        self.paddle_conf = QDoubleSpinBox()
        self.paddle_conf.setRange(0.0, 1.0)
        self.paddle_conf.setSingleStep(0.05)
        self.paddle_conf.setValue(self.cfg["paddle_min_confidence"])
        form.addRow("Paddle min confidence", self.paddle_conf)

        self.vlm_url = QLineEdit(self.cfg["vlm_url"])
        form.addRow("VLM URL (OpenAI-compat)", self.vlm_url)

        self.vlm_model = QLineEdit(self.cfg["vlm_model"])
        form.addRow("VLM model name", self.vlm_model)

        self.hotkey_region = QLineEdit(self.cfg["hotkey_region"])
        form.addRow("Hotkey: pick region (Win)", self.hotkey_region)

        self.hotkey_overlay = QLineEdit(self.cfg["hotkey_overlay"])
        form.addRow("Hotkey: toggle overlay (Win)", self.hotkey_overlay)

        self.hotkey_edit = QLineEdit(self.cfg["hotkey_edit"])
        form.addRow("Hotkey: edit positions (Win)", self.hotkey_edit)

        self.audio_enabled = QCheckBox()
        self.audio_enabled.setChecked(self.cfg["audio_enabled"])
        form.addRow("Audio pipeline (Windows)", self.audio_enabled)

        self.whisper_model = QComboBox()
        self.whisper_model.addItems(WHISPER_MODELS)
        self.whisper_model.setCurrentText(self.cfg["whisper_model"])
        form.addRow("Whisper model", self.whisper_model)

        self.whisper_device = QComboBox()
        self.whisper_device.addItems(["auto", "cuda", "cpu"])
        self.whisper_device.setCurrentText(self.cfg["whisper_device"])
        form.addRow("Whisper device", self.whisper_device)

        self.audio_use_translator = QCheckBox(
            "transcribe JA, then use text translator (instead of whisper translate)")
        self.audio_use_translator.setChecked(self.cfg["audio_use_translator"])
        form.addRow("Audio translation", self.audio_use_translator)
        return box

    def _translation_tab(self):
        box = QGroupBox()
        form = QFormLayout(box)

        self.translator = QComboBox()
        self.translator.addItems(TRANSLATORS)
        self.translator.setCurrentText(self.cfg["translator"])
        form.addRow("Backend", self.translator)

        self.deepl_key = QLineEdit(self.cfg["deepl_api_key"])
        self.deepl_key.setEchoMode(QLineEdit.EchoMode.Password)
        form.addRow("DeepL API key", self.deepl_key)

        self.server_url = QLineEdit(self.cfg["server_url"])
        form.addRow("Server URL (OpenAI-compat)", self.server_url)

        self.server_model = QLineEdit(self.cfg["server_model"])
        form.addRow("Server model name", self.server_model)
        return box

    def _output_tab(self):
        box = QGroupBox()
        form = QFormLayout(box)

        self.out_overlay = QCheckBox()
        self.out_overlay.setChecked(self.cfg["output_overlay"])
        form.addRow("Overlay", self.out_overlay)

        self.show_original = QCheckBox()
        self.show_original.setChecked(self.cfg["show_original"])
        form.addRow("Show original JA text", self.show_original)

        self.font_size = QSpinBox()
        self.font_size.setRange(10, 48)
        self.font_size.setValue(self.cfg["overlay_font_size"])
        form.addRow("Overlay font size", self.font_size)

        self.opacity = QDoubleSpinBox()
        self.opacity.setRange(0.2, 1.0)
        self.opacity.setSingleStep(0.05)
        self.opacity.setValue(self.cfg["overlay_opacity"])
        form.addRow("Overlay background opacity", self.opacity)

        self.out_tts = QCheckBox()
        self.out_tts.setChecked(self.cfg["output_tts"])
        form.addRow("TTS (kokoro-onnx)", self.out_tts)

        self.tts_model = QLineEdit(self.cfg["tts_model_path"])
        form.addRow("TTS model path", self.tts_model)

        self.tts_voices = QLineEdit(self.cfg["tts_voices_path"])
        form.addRow("TTS voices path", self.tts_voices)

        self.tts_voice = QLineEdit(self.cfg["tts_voice"])
        form.addRow("TTS voice", self.tts_voice)

        self.out_file = QCheckBox()
        self.out_file.setChecked(self.cfg["output_file"])
        form.addRow("Write to file", self.out_file)

        self.file_path = QLineEdit(self.cfg["output_file_path"])
        form.addRow("File path", self.file_path)
        return box

    # ---------------------------------------------------------------- result

    def result_config(self) -> dict:
        c = self.cfg
        c["ocr_enabled"] = self.ocr_enabled.isChecked()
        c["poll_interval"] = self.poll.value()
        c["frame_change_ratio"] = self.frame_change.value()
        c["ocr_backend"] = self.ocr_backend.currentText()
        c["paddle_min_confidence"] = self.paddle_conf.value()
        c["vlm_url"] = self.vlm_url.text().strip()
        c["vlm_model"] = self.vlm_model.text().strip()
        c["hotkey_region"] = self.hotkey_region.text().strip()
        c["hotkey_overlay"] = self.hotkey_overlay.text().strip()
        c["hotkey_edit"] = self.hotkey_edit.text().strip()
        c["audio_enabled"] = self.audio_enabled.isChecked()
        c["whisper_model"] = self.whisper_model.currentText()
        c["whisper_device"] = self.whisper_device.currentText()
        c["audio_use_translator"] = self.audio_use_translator.isChecked()
        c["translator"] = self.translator.currentText()
        c["deepl_api_key"] = self.deepl_key.text().strip()
        c["server_url"] = self.server_url.text().strip()
        c["server_model"] = self.server_model.text().strip()
        c["output_overlay"] = self.out_overlay.isChecked()
        c["show_original"] = self.show_original.isChecked()
        c["overlay_font_size"] = self.font_size.value()
        c["overlay_opacity"] = self.opacity.value()
        c["output_tts"] = self.out_tts.isChecked()
        c["tts_model_path"] = self.tts_model.text().strip()
        c["tts_voices_path"] = self.tts_voices.text().strip()
        c["tts_voice"] = self.tts_voice.text().strip()
        c["output_file"] = self.out_file.isChecked()
        c["output_file_path"] = self.file_path.text().strip()
        return c
