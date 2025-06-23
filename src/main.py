import sys
import os
from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QIcon

from window import MainWindow

if __name__ == "__main__":
    app = QApplication([])
    
    # Set application name and icon
    app.setApplicationName("Valorant AFK bot")
    
    # Check for assets directory and set icon if it exists
    icon_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), "assets", "icon.png")
    if os.path.exists(icon_path):
        app.setWindowIcon(QIcon(icon_path))
    
    window = MainWindow(None, Qt.WindowType.Widget)
    window.show()
    
    sys.exit(app.exec())