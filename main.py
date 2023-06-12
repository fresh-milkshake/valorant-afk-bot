from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QApplication

from window import MainWindow

if __name__ == "__main__":
    app = QApplication([])
    window = MainWindow(None, Qt.WindowType.Widget)
    window.show()
    app.exec()
