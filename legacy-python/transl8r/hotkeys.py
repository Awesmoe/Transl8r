"""Global hotkeys on Windows via RegisterHotKey + Qt's native event filter.
No-ops gracefully on other platforms. Combos look like "ctrl+alt+r".
"""

import sys

from PySide6.QtCore import QAbstractNativeEventFilter, QObject, Signal

WM_HOTKEY = 0x0312
MOD_NOREPEAT = 0x4000
MODS = {"alt": 0x1, "ctrl": 0x2, "shift": 0x4, "win": 0x8}


def _parse_combo(combo: str):
    """'ctrl+alt+r' -> (modifier_mask, virtual_key) or (None, None)."""
    mods, vk = 0, None
    for part in (p.strip().lower() for p in combo.split("+")):
        if part in MODS:
            mods |= MODS[part]
        elif len(part) == 1 and part.isalnum():
            vk = ord(part.upper())
        elif part.startswith("f") and part[1:].isdigit():
            vk = 0x70 + int(part[1:]) - 1  # F1..F24
    return (mods, vk) if vk is not None else (None, None)


class _Filter(QAbstractNativeEventFilter):
    def __init__(self, owner):
        super().__init__()
        self.owner = owner

    def nativeEventFilter(self, event_type, message):
        if event_type == b"windows_generic_MSG":
            import ctypes
            import ctypes.wintypes as wt
            msg = wt.MSG.from_address(int(message))
            if msg.message == WM_HOTKEY:
                name = self.owner._ids.get(int(msg.wParam))
                if name:
                    self.owner.triggered.emit(name)
                    return True, 0
        return False, 0


class GlobalHotkeys(QObject):
    """Register named global hotkeys; emits triggered(name) on press."""

    triggered = Signal(str)

    def __init__(self, qapp):
        super().__init__()
        self.qapp = qapp
        self._ids: dict[int, str] = {}
        self._next_id = 1
        self._filter = None
        self.available = sys.platform == "win32"

    def set_bindings(self, bindings: dict[str, str]) -> list[str]:
        """Replace all bindings ({name: combo}); returns combos that failed
        to register (already taken by another app, or unparseable)."""
        if not self.available:
            return []
        import ctypes
        user32 = ctypes.windll.user32

        for hk_id in self._ids:
            user32.UnregisterHotKey(None, hk_id)
        self._ids.clear()

        failed = []
        for name, combo in bindings.items():
            if not combo:
                continue
            mods, vk = _parse_combo(combo)
            if vk is None:
                failed.append(combo)
                continue
            hk_id = self._next_id
            self._next_id += 1
            if user32.RegisterHotKey(None, hk_id, mods | MOD_NOREPEAT, vk):
                self._ids[hk_id] = name
            else:
                failed.append(combo)

        if self._filter is None and self._ids:
            self._filter = _Filter(self)
            self.qapp.installNativeEventFilter(self._filter)
        return failed

    def unregister_all(self):
        if self.available and self._ids:
            import ctypes
            for hk_id in self._ids:
                ctypes.windll.user32.UnregisterHotKey(None, hk_id)
            self._ids.clear()
