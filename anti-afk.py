import threading
import random

import win32gui
import win32con
import win32api

from PyQt6.QtWidgets import (QMainWindow, QApplication, QWidget, QPushButton,
                             QLabel, QComboBox, QSizePolicy, QVBoxLayout,
                             QHBoxLayout, QGroupBox, QTextEdit, QLineEdit)
from PyQt6.QtGui import QFont, QKeySequence, QTextOption, QCloseEvent, QIntValidator, QDoubleValidator
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

    def __init__(self,
                 mode: str,
                 hwnd: Handle,
                 key_press_time: float = DEFAULT_PRESS_TIME):
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

        if self.mode == self.HEAVY_MODE:
            self.path = None
            self._wasd_sequence = []
            self._key_press_time = key_press_time

    @property
    def _jump_delay_diff(self):
        '''Returns the 1/4 of the jump delay'''
        return self._jump_delay / 4

    def update_settings(self, settings: dict):
        '''Updates the settings of the AntiAFK thread'''
        print(settings)
        if self.mode == self.LIGHT_MODE:
            self._jump_delay = float(
                settings.get('light_mode_delay', self._jump_delay))
            return

        self._wasd_sequence = settings.get('heavy_mode_path',
                                           self._wasd_sequence)
        self._key_press_time = float(
            settings.get('heavy_mode_delay', self._key_press_time))
        

    @property
    def jump_delay(self):
        '''Returns a random delay between the jump delay +- jump delay difference'''
        return random.uniform(self._jump_delay - self._jump_delay_diff,
                              self._jump_delay + self._jump_delay_diff)

    def send_key(self, key: Hexadecimal, delay: float):
        '''Sends a key to the game'''
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(delay)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def light_mode(self):
        '''
        Runs the light mode - just jumps with random delay between jumps and
        rare other key presses and mouse movements
        '''

        while self.running:
            self.send_key(Keys.SPACE, self.DEFAULT_PRESS_TIME)
            wait(self.jump_delay)

    def heavy_mode(self):
        '''
        Runs the heavy mode - moves along a random path and rarely does random
        jumps and mouse movements
        '''

        while self.running:
            self.path = [(Keys.W, self._key_press_time),
                         (Keys.A, self._key_press_time),
                         (Keys.S, self._key_press_time),
                         (Keys.D, self._key_press_time)]

            for key, time in self.path:
                self.send_key(key, time)

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
        self._anti_afk_settings = {}
        self._anti_afk_mode = AntiAFK.LIGHT_MODE
        self._console_open = False
        self._valorant_status = False

        #########################
        # Thread controls group #
        #########################

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

        ###############################
        # Settings for AntiAFK thread #
        ###############################

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

        # Creating hint QLabel and explaintaions for each mode
        self.light_mode_explanation = "Sending <b>SPACE</b> key press to VALORANT window every <b>N</b> seconds"
        # self.heavy_mode_explanation = "Sending sequence of keys to VALORANT like you are playing (WASD + SPACE) every N seconds"
        self.heavy_mode_explanation = "Sending sequence of keys to <b>VALORANT</b> like you are playing <i>(WASD + SPACE)</i> every <b>N</b> seconds"
        self.hint_label = QLabel(self.light_mode_explanation)
        self.hint_label.setWordWrap(True)
        self.hint_label.setAlignment(Qt.AlignmentFlag.AlignLeft)

        # Settings for each mode of AntiAFK thread (light and heavy)
        # Light mode:
        # - Delay between SPACE key presses
        # Heavy mode:
        # - Delay between pressing the key and unpressing it
        # - Path to move along (consists of 4 directions: W, A, S, D)

        # Light mode settings
        light_mode_delay_label = QLabel("Space delay:")
        light_mode_delay_input = QLineEdit()
        light_mode_delay_input.setPlaceholderText("5.0")
        light_mode_delay_input.setValidator(QDoubleValidator(0.0, 10, 4))
        light_mode_delay_input.textChanged.connect(
            self.change_light_mode_delay)

        light_mode_settings_layout = QHBoxLayout()
        light_mode_settings_layout.addWidget(light_mode_delay_label)
        light_mode_settings_layout.addWidget(light_mode_delay_input)

        self.light_mode_settings_group = QGroupBox("Light mode settings")
        self.light_mode_settings_group.setLayout(light_mode_settings_layout)

        # Heavy mode settings
        heavy_mode_delay_label = QLabel("Key delay:")
        heavy_mode_delay_input = QLineEdit()
        heavy_mode_delay_input.setPlaceholderText("0,5")
        heavy_mode_delay_input.setValidator(QDoubleValidator(0.0, 10, 4))
        heavy_mode_delay_input.textChanged.connect(
            self.change_heavy_mode_delay)

        heavy_mode_path_label = QLabel("Path:")
        heavy_mode_path_input = QLineEdit()
        heavy_mode_path_input.setPlaceholderText("WASD")
        heavy_mode_path_input.textChanged.connect(self.change_heavy_mode_path)

        heavy_mode_settings_layout = QVBoxLayout()
        heavy_mode_settings_layout.addWidget(heavy_mode_delay_label)
        heavy_mode_settings_layout.addWidget(heavy_mode_delay_input)
        heavy_mode_settings_layout.addWidget(heavy_mode_path_label)
        heavy_mode_settings_layout.addWidget(heavy_mode_path_input)

        self.heavy_mode_settings_group = QGroupBox("Heavy mode settings")
        self.heavy_mode_settings_group.setLayout(heavy_mode_settings_layout)
        self.heavy_mode_settings_group.hide()

        # Layout for all settings
        self.settings_layout = QVBoxLayout()
        self.settings_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.settings_layout.addWidget(self.window_status_label)
        self.settings_layout.addLayout(self.mode_layout)
        self.settings_layout.addWidget(self.hint_label)
        self.settings_layout.addWidget(self.light_mode_settings_group)
        self.settings_layout.addWidget(self.heavy_mode_settings_group)

        # Group for the settings
        settings_group = QGroupBox("Settings")
        settings_group.setLayout(self.settings_layout)

        ##############################
        # Main layout for the window #
        ##############################

        # Main layout for the window (controls and settings)
        self.main_layout = QHBoxLayout()
        self.main_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.main_layout.addWidget(self.controls_group)
        self.main_layout.addWidget(settings_group)

        # Main central widget for the window
        self.main_widget = QWidget()
        self.main_widget.setLayout(self.main_layout)

        self.setCentralWidget(self.main_widget)

        # Run valorant_status every 5 seconds to
        # refresh the VALORANT status label
        self.valorant_status_timer = QTimer()
        self.valorant_status_timer.timeout.connect(self.valorant_status)
        self.valorant_status_timer.start(5000)

    def find_window(self, window_name) -> Handle | None:
        '''Returns the window handle if found, None if not'''
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

        # Decide which label hint to show based on the current mode
        # and update the hint label. Also, show or hide the settings
        # for the current mode
        if self._anti_afk_mode == AntiAFK.LIGHT_MODE:
            self.hint_label.setText(self.light_mode_explanation)
            self.light_mode_settings_group.show()
            self.heavy_mode_settings_group.hide()
        elif self._anti_afk_mode == AntiAFK.HEAVY_MODE:
            self.hint_label.setText(self.heavy_mode_explanation)
            self.light_mode_settings_group.hide()
            self.heavy_mode_settings_group.show()

    def update_aafk_settings(self, **kwargs: dict):
        '''Updates the settings of the AntiAFK thread'''
        self.log(f"Updating AntiAFK settings: {kwargs}...")
        self._anti_afk_settings.update(kwargs)
        if self.aafk:
            self.aafk.update_settings(self._anti_afk_settings)

    def change_light_mode_delay(self, delay):
        delay = delay.strip().replace(",", ".")
        '''Changes the delay of the AntiAFK thread'''
        if not delay or not delay.isdigit():
            return

        self.log(f"Changing AntiAFK delay to {delay}...")
        self.update_aafk_settings(light_mode_delay=delay)

    def change_heavy_mode_delay(self, delay):
        delay = delay.strip().replace(",", ".")
        if not delay or not delay.isdigit():
            return
        '''Changes the delay of the AntiAFK thread'''
        self.log(f"Changing AntiAFK delay to {delay}...")
        self.update_aafk_settings(heavy_mode_delay=delay)

    def change_heavy_mode_path(self, path):
        '''Changes the path of the AntiAFK thread'''
        self.log(f"Changing AntiAFK path to {path}...")
        self.update_aafk_settings(heavy_mode_path=path)

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
