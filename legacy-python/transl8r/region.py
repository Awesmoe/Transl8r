"""Fullscreen drag-to-select region picker. Returns physical-pixel rect for mss.

Also draws any already-configured regions as labeled outlines, so you can see
where existing capture boxes sit while drawing a new one.
"""

from PySide6.QtCore import QPoint, QRect, Qt, Signal
from PySide6.QtGui import QColor, QFont, QGuiApplication, QPainter, QPen
from PySide6.QtWidgets import QWidget


class RegionSelector(QWidget):
    region_selected = Signal(dict)   # {"left","top","width","height"} physical px
    cancelled = Signal()

    def __init__(self, regions: list[dict] | None = None):
        super().__init__()
        self.start = None
        self.end = None
        self.regions = regions or []

        geo = QRect()
        for screen in QGuiApplication.screens():
            geo = geo.united(screen.geometry())
        self.setGeometry(geo)

        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setCursor(Qt.CursorShape.CrossCursor)

    def _logical_rect(self, region: dict) -> QRect:
        """Inverse of the save-path math: physical px -> widget-local logical
        rect. Divide by the DPR of the screen containing the region (found the
        same way the save path uses screenAt), then map global->widget."""
        cx = region["left"] + region["width"] / 2
        cy = region["top"] + region["height"] / 2
        dpr = QGuiApplication.primaryScreen().devicePixelRatio()
        for screen in QGuiApplication.screens():
            d = screen.devicePixelRatio()
            if screen.geometry().contains(int(cx / d), int(cy / d)):
                dpr = d
                break
        top_left = self.mapFromGlobal(
            QPoint(int(region["left"] / dpr), int(region["top"] / dpr)))
        return QRect(top_left.x(), top_left.y(),
                     int(region["width"] / dpr), int(region["height"] / dpr))

    def paintEvent(self, _event):
        p = QPainter(self)
        p.fillRect(self.rect(), QColor(0, 0, 0, 100))

        # existing regions: dashed amber outline + label, drawn under the
        # live selection so the new box reads clearly on top.
        if self.regions:
            label_font = QFont()
            label_font.setPixelSize(13)
            label_font.setBold(True)
            for i, region in enumerate(self.regions):
                rect = self._logical_rect(region)
                p.setPen(QPen(QColor(255, 180, 40), 2, Qt.PenStyle.DashLine))
                p.drawRect(rect)
                p.setFont(label_font)
                p.setPen(QColor(255, 210, 120))
                p.drawText(rect.adjusted(4, 3, -4, -4),
                           Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft,
                           f"Region {i + 1}")

        if self.start and self.end:
            sel = QRect(self.start, self.end).normalized()
            p.setCompositionMode(QPainter.CompositionMode.CompositionMode_Clear)
            p.fillRect(sel, Qt.GlobalColor.transparent)
            p.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceOver)
            p.setPen(QPen(QColor(0, 200, 255), 2))
            p.drawRect(sel)
        p.end()

    def mousePressEvent(self, event):
        self.start = event.position().toPoint()
        self.end = self.start
        self.update()

    def mouseMoveEvent(self, event):
        if self.start:
            self.end = event.position().toPoint()
            self.update()

    def mouseReleaseEvent(self, event):
        self.end = event.position().toPoint()
        sel = QRect(self.start, self.end).normalized()
        self.close()
        if sel.width() > 10 and sel.height() > 10:
            center = self.mapToGlobal(sel.center())
            screen = (QGuiApplication.screenAt(center)
                      or QGuiApplication.primaryScreen())
            dpr = screen.devicePixelRatio()
            top_left = self.mapToGlobal(sel.topLeft())
            self.region_selected.emit({
                "left": int(top_left.x() * dpr),
                "top": int(top_left.y() * dpr),
                "width": int(sel.width() * dpr),
                "height": int(sel.height() * dpr),
            })
        else:
            self.cancelled.emit()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_Escape:
            self.close()
            self.cancelled.emit()
