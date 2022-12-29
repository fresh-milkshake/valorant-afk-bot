import threading
import random
import typing
import time

import win32gui
import win32con
import win32api
import win32ui

from PyQt6.QtWidgets import *
from PyQt6.QtCore import *
from PyQt6.QtGui import *


Handle = int
Binary = int

class Path:

    def __init__(self, length, variation):
        '''
        Creates a new path with a given length and variation
        :param length: The length of the path in pressed keys
        :param variation: The variation of the path (percentage of differring keys)
        '''
        self.length = length
        self.variation = variation

        self.generate()

    def generate(self):
        '''Generates a random path depending on the length and variation'''
        self.path = []
        # for i in range(self.length):
        #     self.path.append(Direction.random(self.variation))
        self.path.append('w' * self.length)
        self.path.append('s' * self.length)

    def to_wasd_sequence(self) -> str:
        '''Converts the path to a string WASD sequence'''
        return ''.join(self.path)


class AntiAFK(threading.Thread):
    LIGHT_MODE = 'Light'
    HEAVY_MODE = 'Heavy'
    AVALIABLE_MODES = [LIGHT_MODE, HEAVY_MODE]
    DEFAULT_PRESS_TIME = .1

    def __init__(self, mode: str, hwnd: Handle):
        '''
        Creates a new AntiAFK thread
        :param mode: The mode of the AntiAFK thread
        :param hwnd: The window handle of the game
        '''

        if mode not in self.AVALIABLE_MODES:
            raise ValueError(f"Invalid mode: {mode}")

        if not win32gui.IsWindow(hwnd):
            raise ValueError(f"Invalid window handle: {hwnd}")

        self.mode = mode
        self.valorant_hwnd = hwnd

        super().__init__()
        self.running = False

        self._jump_delay = 5
        self._jump_delay_diff = 3

        if self.mode == self.HEAVY_MODE:
            self.move_keys = 'wasd'
            self._wasd_sequence = []

    @property
    def jump_delay(self):
        '''Returns a random delay between the jump delay +- jump delay difference'''
        return random.randint(self._jump_delay - self._jump_delay_diff,
                              self._jump_delay + self._jump_delay_diff)

    def send_key(self, key: Binary, times: int = DEFAULT_PRESS_TIME):
        '''Sends a key to the game'''
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        time.sleep(times)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def run(self):
        '''Starts the anti afk thread'''
        self.running = True
        win = win32ui.CreateWindowFromHandle(self.valorant_hwnd)
        while self.running:
            self.send_key(win32con.VK_SPACE)
            time.sleep(self.jump_delay)

    def stop(self):
        '''Stops the anti afk thread'''
        self.running = False


class MainWindow(QMainWindow):

    class AntiAFKStatus:
        NOT_WORKING = "<font color='red'>Not working</font>"
        WORKING = "<font color='green'>Working</font>"

    class VALORANTStatus:
        NOT_FOUND = "<font color='red'>Not found</font>"
        FOUND = "<font color='green'>Found</font>"

    def __init__(self, parent, flags):
        super().__init__(parent, flags)

        self.setWindowTitle("Anti-AFK")
        self.setSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Minimum)
        self.setMinimumSize(self.minimumSizeHint())
        self.closeEvent = self.close

        self.aafk = None
        self._anti_afk_status = False
        self._anti_afk_mode = AntiAFK.LIGHT_MODE
        self._console_open = False
        self._valorant_status = False

        # Start button for creating new thread AntiAFK
        self.start_button = QPushButton("Start")
        self.start_button.setShortcut(QKeySequence(""))
        self.start_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.start_button.clicked.connect(self.start_anti_afk)

        # Stop button for stopping the AntiAFK thread
        self.stop_button = QPushButton("Stop")
        self.stop_button.setEnabled(False)
        self.stop_button.setShortcut(QKeySequence(""))
        self.stop_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.stop_button.clicked.connect(self.stop_anti_afk)

        self.status_label = QLabel(f"Status: {self.AntiAFKStatus.NOT_WORKING}")
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

        # Button for opening the console
        self.console_button = QPushButton("Open console")
        self.console_button.setStyleSheet("color: gray;")
        self.console_button.setShortcut(QKeySequence(""))
        self.console_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.console_button.clicked.connect(self.toggle_console)

        # Bottom console for logging and stuff
        self.console = QTextEdit()
        self.console.setReadOnly(True)
        self.console.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        self.console.setWordWrapMode(QTextOption.WrapMode.NoWrap)
        self.console.setFont(QFont("Consolas", 10))
        self.console.setStyleSheet("background-color: black;"
                                   "color: white; border-radius: 5px;")
        self.console.hide()

        # Layout for changing current state of the AntiAFK thread
        self.controls_layout = QVBoxLayout()
        self.controls_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.controls_layout.addWidget(self.start_button)
        self.controls_layout.addWidget(self.stop_button)
        self.controls_layout.addWidget(self.console_button)
        self.controls_layout.addWidget(self.status_label)
        self.controls_layout.addWidget(self.console)

        # Group for the controls
        controls_group = QGroupBox("Controls")
        controls_group.setLayout(self.controls_layout)

        # VALORANT window status
        self.window_status_label = QLabel()
        self.log(f"VALORANT window status: {self.valorant_status}")

        # Mode selection
        mode_label = QLabel("Mode:")
        mode_input = QComboBox()
        mode_input.addItems(AntiAFK.AVALIABLE_MODES)
        mode_input.currentTextChanged.connect(self.change_mode)

        # Layout for the mode selection
        self.mode_layout = QHBoxLayout()
        self.mode_layout.addWidget(mode_label)
        self.mode_layout.addWidget(mode_input)

        # Layout for all settings
        self.settings_layout = QVBoxLayout()
        self.settings_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.settings_layout.addWidget(self.window_status_label)
        self.settings_layout.addLayout(self.mode_layout)

        # Group for the settings
        settings_group = QGroupBox("Settings")
        settings_group.setLayout(self.settings_layout)

        # Main layout for the window (controls and settings)
        self.main_layout = QHBoxLayout()
        self.main_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        # self.main_layout.setSizeConstraint(QLayout.SizeConstraint.SetMinimumSize)
        self.main_layout.addWidget(controls_group)
        self.main_layout.addWidget(settings_group)

        # Main central widget for the window
        self.main_widget = QWidget()
        self.main_widget.setLayout(self.main_layout)

        self.setCentralWidget(self.main_widget)

    def find_window(self, window_name) -> Handle:
        windows = []
        win32gui.EnumWindows(lambda hwnd, windows: windows.append(hwnd), windows)
        for window in windows:
            if window_name in win32gui.GetWindowText(window):
                return window

    @property
    def valorant_status(self):
        '''Returns True if VALORANT is found, False if not'''
        window = self.find_window("VALORANT")
        if window:
            self.window_status_label.setText(f"VALORANT: {self.VALORANTStatus.FOUND}")
            self.controls_layout.setEnabled(True)
            return True
        else:
            self.window_status_label.setText(f"VALORANT: {self.VALORANTStatus.NOT_FOUND}")
            self.controls_layout.setEnabled(False)
            return False

    @property
    def anti_afk_status(self):
        '''Returns True if AntiAFK is working, False if not'''
        return self._anti_afk_status

    @anti_afk_status.setter
    def anti_afk_status(self, value):
        '''Sets the status of the AntiAFK thread and updates the status label'''
        self.log(f"AntiAFK status: {value}")
        self._anti_afk_status = value
        if self._anti_afk_status:
            self.status_label.setText(f"Status: {self.AntiAFKStatus.WORKING}")
        else:
            self.status_label.setText(f"Status: {self.AntiAFKStatus.NOT_WORKING}")

    def change_mode(self, mode):
        '''Changes the mode of the AntiAFK thread'''
        self.log(f"Changing AantiAFK mode to {mode}...")
        self._anti_afk_mode = mode

    def toggle_console(self):
        '''Opens the console if it's closed, closes it if it's open'''
        if not self._console_open:
            self.console_button.setText("Close console")
            self.console.show()
            self._console_open = True
        else:
            self.console_button.setText("Open console")
            self.console.hide()
            self._console_open = False
            # self.adjustSize() TODO: make it work and read docs about this method

    def log(self, text):
        '''Logs text to the console'''
        self.console.append(text)
        self.console.verticalScrollBar().setValue(
            self.console.verticalScrollBar().maximum())

    def start_anti_afk(self):
        self.log(f'Starting AntiAFK with mode "{self._anti_afk_mode}"...')
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)
        self.aafk = AntiAFK(mode=self._anti_afk_mode, hwnd=self.find_window("VALORANT"))
        self.anti_afk_status = True
        self.aafk.start()

    def stop_anti_afk(self):
        self.log("Killing AntiAFK...")
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.anti_afk_status = False
        self.aafk.stop()

    def close(self, event):
        '''Stops the AntiAFK thread when the window is closed'''
        self.stop_anti_afk()
        event.accept()

if __name__ == "__main__":
    app = QApplication([])
    window = MainWindow(None, Qt.WindowType.Widget)
    window.show()
    app.exec()