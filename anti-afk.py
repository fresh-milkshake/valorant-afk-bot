import threading
import random

import win32gui
import win32con
import win32api

from PyQt6.QtWidgets import (QMainWindow, QApplication, QWidget, QPushButton, QLabel, QComboBox,
                             QSizePolicy, QVBoxLayout, QHBoxLayout, QGroupBox, QTextEdit)
from PyQt6.QtGui import QFont, QKeySequence, QTextOption, QCloseEvent
from PyQt6.QtCore import Qt, QTimer
from typing import List, Tuple

from time import sleep as wait


# Type aliases for better readability
Handle = int
Hexadecimal = int


# Bindings for the keys and their hexadecimal values
# in WIN32 API
class Keys:
    W = 0x57
    A = 0x41
    S = 0x53
    D = 0x44
    SPACE = 0x20


# Main class implemeting Thread classe's run and
# stop methods for sending key presses to the game
# separated from the GUI
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
            self.path = None
            self._wasd_sequence = []

    @property
    def jump_delay(self):
        '''Returns a random delay between the jump delay +- jump delay difference'''
        return random.randint(self._jump_delay - self._jump_delay_diff,
                              self._jump_delay + self._jump_delay_diff)

    def send_key(self, key: Hexadecimal, times: float = DEFAULT_PRESS_TIME):
        '''Sends a key to the game'''
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(times)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def light_mode(self):
        '''
        Runs the light mode - just jumps with random delay between jumps and
        rare other key presses and mouse movements
        '''

        while self.running:
            self.send_key(Keys.SPACE)
            wait(self.jump_delay)

    def heavy_mode(self):
        '''
        Runs the heavy mode - moves along a random path and rarely does random
        jumps and mouse movements
        '''

        while self.running:
            self.path = [(Keys.W, self.DEFAULT_PRESS_TIME),(Keys.A, self.DEFAULT_PRESS_TIME),
                         (Keys.S, self.DEFAULT_PRESS_TIME),(Keys.D, self.DEFAULT_PRESS_TIME)]

            for key, time in self.path:
                self.send_key(key, time)
                wait(self.DEFAULT_PRESS_TIME)

    def run(self):
        '''Starts the anti afk thread'''
        self.running = True
        if self.mode == self.LIGHT_MODE:
            self.light_mode()
        elif self.mode == self.HEAVY_MODE:
            self.heavy_mode()

    def stop(self):
        '''Stops the anti afk thread'''
        self.running = False


# GUI class for the AntiAFK program powered by PyQt6
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
        self.setSizePolicy(QSizePolicy.Policy.Minimum,
                           QSizePolicy.Policy.Minimum)
        self.setMinimumSize(self.minimumSizeHint())
        self.closeEvent = self.close

        # Define variables for the AntiAFK thread
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
        self.console = QTextEdit('')
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
        self.controls_group = QGroupBox("Controls")
        self.controls_group.setLayout(self.controls_layout)

        # VALORANT window status
        self.window_status_label = QLabel()
        self.log(f"VALORANT window status: {self.valorant_status()}")

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
        self.main_layout.addWidget(self.controls_group)
        self.main_layout.addWidget(settings_group)

        # Main central widget for the window
        self.main_widget = QWidget()
        self.main_widget.setLayout(self.main_layout)

        self.setCentralWidget(self.main_widget)

        # Run valorant_status every 5 seconds
        self.valorant_status_timer = QTimer()
        self.valorant_status_timer.timeout.connect(self.valorant_status)
        self.valorant_status_timer.start(5000)

    def find_window(self, window_name) -> Handle | None:
        windows = []
        win32gui.EnumWindows(lambda hwnd, windows: windows.append(hwnd),
                             windows)
        for window in windows:
            if window_name in win32gui.GetWindowText(window):
                return window


    def valorant_status(self):
        '''Returns True if VALORANT is found, False if not'''
        window = self.find_window("VALORANT")
        if window:
            self.window_status_label.setText(
                f"VALORANT: {self.VALORANTStatus.FOUND}")
            self.controls_layout.setEnabled(True)
            return True
        else:
            self.window_status_label.setText(
                f"VALORANT: {self.VALORANTStatus.NOT_FOUND}")
            self.controls_group.setEnabled(False)
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
            self.status_label.setText(
                f"Status: {self.AntiAFKStatus.NOT_WORKING}")

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
        window = self.find_window("VALORANT")
        assert window, "VALORANT window not found"

        self.log(f'Starting AntiAFK with mode "{self._anti_afk_mode}"...')
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

        self.aafk = AntiAFK(mode=self._anti_afk_mode, hwnd=window)
        self.anti_afk_status = True
        self.aafk.start()

    def stop_anti_afk(self):
        assert self.aafk, "AntiAFK not created"

        self.log("Killing AntiAFK...")
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.anti_afk_status = False
        self.aafk.stop()

    def close(self, a0: QCloseEvent):
        '''Stops the AntiAFK thread when the window is closed'''
        if self.aafk:
            self.log("Killing AntiAFK...")
            self.stop_anti_afk()
        a0.accept()


if __name__ == "__main__":
    app = QApplication([])
    window = MainWindow(None, Qt.WindowType.Widget)
    window.show()
    app.exec()