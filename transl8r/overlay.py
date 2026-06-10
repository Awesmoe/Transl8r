"""Always-on-top, click-through, translucent overlay that shows translations.

Edit mode: temporarily drops click-through so the overlay can be dragged to a
new position. The position is stored as an OFFSET from the overlay's computed
base spot (just below its region, or bottom-center for audio), so re-picking a
region carries the placed box along instead of orphaning it.
"""

import sys

from PySide6.QtCore import Qt, Signal, Slot
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import QLabel, QVBoxLayout, QWidget

_PLACEHOLDER = "⠿  drag me  ⠿"


class Overlay(QWidget):
    """One overlay window. region=None -> bottom-center (audio pipeline).

    offset is a (dx, dy) logical-pixel nudge applied on top of the computed
    base position; emitted back via `moved` after the user drags in edit mode.
    """

    moved = Signal()  # offset changed via drag; read self.offset

    def __init__(self, cfg: dict, region: dict | None = None,
                 offset: tuple[int, int] = (0, 0)):
        super().__init__()
        self.cfg = cfg
        self.region = region
        self.offset = (int(offset[0]), int(offset[1]))

        self._edit = False
        self._drag_origin = None
        self._has_text = False
        self._showing_placeholder = False

        flags = (Qt.WindowType.FramelessWindowHint
                 | Qt.WindowType.WindowStaysOnTopHint
                 | Qt.WindowType.Tool)
        # On Windows we DON'T set WindowTransparentForInput: Qt re-applies its
        # window flags to the native window on activation (e.g. when the box is
        # clicked in edit mode), which would clobber the native WS_EX_TRANSPARENT
        # we toggle for edit mode and snap the box back to click-through. Instead
        # we own click-through natively via _apply_clickthrough, re-asserted
        # after every show(). On other platforms the Qt flag is the mechanism.
        if sys.platform != "win32":
            flags |= Qt.WindowType.WindowTransparentForInput
        self.setWindowFlags(flags)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)

        self.orig_label = QLabel()
        self.orig_label.setWordWrap(True)
        self.trans_label = QLabel()
        self.trans_label.setWordWrap(True)
        layout.addWidget(self.orig_label)
        layout.addWidget(self.trans_label)

        # The labels fill the whole window; without this they'd intercept the
        # mouse press in edit mode and the drag handlers (on this widget) would
        # never get a clean grab. Let every mouse event fall through to us.
        for lbl in (self.orig_label, self.trans_label):
            lbl.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, True)

        self.apply_config(cfg)

    def apply_config(self, cfg: dict):
        self.cfg = cfg
        size = cfg.get("overlay_font_size", 18)
        opacity = cfg.get("overlay_opacity", 0.85)
        bg = f"rgba(10, 10, 10, {int(opacity * 255)})"
        # a dashed border makes the (otherwise click-through) box grabbable in
        # edit mode; no border in normal mode so it stays unobtrusive.
        border = ("border: 2px dashed #00c8ff;" if self._edit
                  else "border: none;")
        common = ("border-radius: 6px; padding: 6px 10px; "
                  f"background-color: {bg}; {border}")
        self.trans_label.setStyleSheet(
            f"QLabel {{ color: #f0f0f0; font-size: {size}px; {common} }}")
        self.orig_label.setStyleSheet(
            f"QLabel {{ color: #9fd6ff; font-size: {max(10, size - 6)}px; {common} }}")
        self.orig_label.setVisible(bool(cfg.get("show_original")))
        self._place()
        # Reflow NOW if we're already on screen, so toggling show-original /
        # font / opacity applies live instead of only after the next show_text
        # (or a manual window nudge). _place fixed the width; adjustSize fits
        # the height to the (possibly newly visible) original-text label.
        if self.isVisible():
            self.adjustSize()

    def _base_pos(self) -> tuple[int, int, int]:
        """Computed (x, y, width) in logical px BEFORE the offset is applied.
        Below the capture region; bottom-center fallback for audio."""
        region = self.region
        screen = QGuiApplication.primaryScreen()
        dpr = screen.devicePixelRatio()
        if region:
            x = int(region["left"] / dpr)
            y = int((region["top"] + region["height"]) / dpr) + 8
            w = max(280, int(region["width"] / dpr))
        else:
            geo = screen.availableGeometry()
            w = int(geo.width() * 0.6)
            x = geo.x() + (geo.width() - w) // 2
            y = geo.y() + geo.height() - 160
        return x, y, w

    def _place(self):
        x, y, w = self._base_pos()
        self.setFixedWidth(w)
        self.move(x + self.offset[0], y + self.offset[1])

    @Slot(str, str)
    def show_text(self, original: str, translated: str):
        self._showing_placeholder = False
        self._has_text = True
        self.orig_label.setText(original)
        self.trans_label.setText(translated)
        self.adjustSize()
        if not self.isVisible():
            self.show()
        # Windows: we own click-through natively (no Qt flag), so re-assert it
        # whenever we show. Click-through in normal mode, off while editing.
        self._apply_clickthrough(not self._edit)
        self._assert_topmost()

    @Slot()
    def clear(self):
        """Dialogue left the screen — clear and hide.

        No-op while editing: the OCR worker keeps running, and a clear() firing
        mid-drag would yank the box out from under the user."""
        if self._edit:
            return
        self._has_text = False
        self.orig_label.clear()
        self.trans_label.clear()
        self.hide()

    # ----------------------------------------------------------- edit mode

    def set_edit_mode(self, on: bool):
        """Flip click-through off (on=True) so the box can be dragged, or back
        on (on=False). Shows a placeholder so empty overlays stay grabbable."""
        self._edit = on
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, not on)
        # On non-Windows the Qt flag is the only lever (dev/testing only). On
        # Windows we DON'T flip it: toggling WindowTransparentForInput at
        # runtime doesn't reliably update the native WS_EX_TRANSPARENT style
        # (clicks kept passing through — observed on Qt 6.11 / Win11), so
        # _apply_clickthrough sets that style directly instead.
        if sys.platform != "win32":
            self.setWindowFlag(Qt.WindowType.WindowTransparentForInput, not on)

        if on:
            self.setCursor(Qt.CursorShape.SizeAllCursor)  # drag affordance
            if not self._has_text:
                self.trans_label.setText(_PLACEHOLDER)
                self._showing_placeholder = True
            self.apply_config(self.cfg)      # border + place
            self.adjustSize()
            self.show()
            self._apply_clickthrough(False)  # editable: stop passing clicks through
            self._place()
            self._assert_topmost()
        else:
            self.unsetCursor()
            if self._showing_placeholder:
                self._showing_placeholder = False
                self.trans_label.clear()
            self.apply_config(self.cfg)      # drop border, place at base+offset
            # Restore click-through AFTER show() — show() can make Qt re-sync the
            # native ex-style (which no longer carries WS_EX_TRANSPARENT), so a
            # pre-show call would get clobbered and the box would block clicks
            # over the game. Mirrors the enter path and show_text/show_if_text.
            if self._has_text:
                self.adjustSize()
                self.show()
                self._apply_clickthrough(True)
                self._assert_topmost()
            else:
                self.hide()
                self._apply_clickthrough(True)  # safe for the next show()

    def show_if_text(self):
        """Re-show (e.g. overlay output toggled back on) if holding text."""
        if self._has_text:
            self.show()
            self._apply_clickthrough(not self._edit)
            self._assert_topmost()

    def _apply_clickthrough(self, enabled: bool):
        """Set (enabled) / clear (edit mode) the native WS_EX_TRANSPARENT bit so
        the overlay does / doesn't pass mouse input through to the game beneath.
        Read-modify-write preserves WS_EX_LAYERED (our translucency) etc."""
        if sys.platform != "win32":
            return
        import ctypes
        from ctypes import wintypes
        GWL_EXSTYLE = -20
        WS_EX_TRANSPARENT = 0x20
        user32 = ctypes.windll.user32
        user32.GetWindowLongW.restype = wintypes.LONG
        user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
        user32.SetWindowLongW.restype = wintypes.LONG
        user32.SetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int,
                                          wintypes.LONG]
        hwnd = int(self.winId())
        style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        if enabled:
            style |= WS_EX_TRANSPARENT
        else:
            style &= ~WS_EX_TRANSPARENT
        user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style)

    def mousePressEvent(self, event):
        if self._edit and event.button() == Qt.MouseButton.LeftButton:
            self._drag_origin = event.globalPosition().toPoint() - self.pos()
            event.accept()

    def mouseMoveEvent(self, event):
        if self._edit and self._drag_origin is not None:
            self.move(event.globalPosition().toPoint() - self._drag_origin)
            event.accept()

    def mouseReleaseEvent(self, event):
        if self._edit and self._drag_origin is not None:
            self._drag_origin = None
            bx, by, _ = self._base_pos()
            pos = self.pos()
            self.offset = (pos.x() - bx, pos.y() - by)
            self.moved.emit()
            event.accept()

    def _assert_topmost(self):
        """Qt's WindowStaysOnTopHint is set once at creation; borderless
        fullscreen games re-grab the top of the z-order afterwards. On
        Windows, re-assert HWND_TOPMOST on every update."""
        if sys.platform != "win32":
            return
        import ctypes
        HWND_TOPMOST = -1
        SWP_NOSIZE, SWP_NOMOVE, SWP_NOACTIVATE = 0x0001, 0x0002, 0x0010
        ctypes.windll.user32.SetWindowPos(
            int(self.winId()), HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE)
