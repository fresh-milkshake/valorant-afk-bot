import win32gui
from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QKeySequence, QTextOption, QFont, QDoubleValidator, QCloseEvent
from PyQt6.QtWidgets import QMainWindow, QSizePolicy, QPushButton, QLabel, QTextEdit, QVBoxLayout, QGroupBox, QComboBox, \
    QHBoxLayout, QLineEdit, QWidget

from sender import Mode, KeySender
from mytypes import Handle


def find_window(window_name) -> Handle | None:
    """Returns the window handle if found, None if not"""
    windows = []
    win32gui.EnumWindows(lambda hwnd, windows: windows.append(hwnd),
                         windows)
    for window in windows:
        if window_name in win32gui.GetWindowText(window):
            return window



class MainWindow(QMainWindow):
    class KeySenderStatus:
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

        # Define variables for the KeySender thread
        self.aafk = None
        self._anti_afk_status = False
        self._anti_afk_settings = {}
        self._anti_afk_mode = Mode.LIGHT
        self._console_open = False
        self._valorant_status = False

        #########################
        # Thread controls group #
        #########################

        # Start button for creating new thread KeySender
        self.start_button = QPushButton("Start")
        self.start_button.setShortcut(QKeySequence(""))
        self.start_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.start_button.clicked.connect(self.start_anti_afk)

        # Stop button for stopping the KeySender thread
        self.stop_button = QPushButton("Stop")
        self.stop_button.setEnabled(False)
        self.stop_button.setShortcut(QKeySequence(""))
        self.stop_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.stop_button.clicked.connect(self.stop_anti_afk)

        self.status_label = QLabel(f"Status: {self.KeySenderStatus.NOT_WORKING}")
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

        # Layout for changing current state of the KeySender thread
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
        # Settings for KeySender thread #
        ###############################

        # VALORANT window status
        self.window_status_label = QLabel()
        self.log(f"VALORANT window status: {self.valorant_status()}")

        # Mode selection
        mode_label = QLabel("Mode:")
        mode_input = QComboBox()
        mode_input.addItems(KeySender.MODES_NAMES)
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

        # Settings for each mode of KeySender thread (light and heavy)
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

    def valorant_status(self):
        """Returns True if VALORANT is found, False if not"""
        window = find_window("VALORANT")
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
        """Returns True if KeySender is working, False if not"""
        return self._anti_afk_status

    @anti_afk_status.setter
    def anti_afk_status(self, value):
        """Sets the status of the KeySender thread and updates the status label"""
        self.log(f"KeySender status: {value}")
        self._anti_afk_status = value
        if self._anti_afk_status:
            self.status_label.setText(f"Status: {self.KeySenderStatus.WORKING}")
        else:
            self.status_label.setText(
                f"Status: {self.KeySenderStatus.NOT_WORKING}")

    def change_mode(self, mode):
        """Changes the mode of the KeySender thread"""
        self.log(f"Changing AantiAFK mode to {mode}...")
        self._anti_afk_mode = mode

        # Decide which label hint to show based on the current mode
        # and update the hint label. Also, show or hide the settings
        # for the current mode
        if self._anti_afk_mode == Mode.LIGHT.value:
            self.hint_label.setText(self.light_mode_explanation)
            self.light_mode_settings_group.show()
            self.heavy_mode_settings_group.hide()
        elif self._anti_afk_mode == Mode.HEAVY.value:
            self.hint_label.setText(self.heavy_mode_explanation)
            self.light_mode_settings_group.hide()
            self.heavy_mode_settings_group.show()

    def update_aafk_settings(self, **kwargs: dict):
        """Updates the settings of the KeySender thread"""
        self.log(f"Updating KeySender settings: {kwargs}...")
        self._anti_afk_settings.update(kwargs)
        if self.aafk:
            self.aafk.update_settings(self._anti_afk_settings)

    def change_light_mode_delay(self, delay):
        """Changes the delay of the KeySender thread"""
        delay = delay.strip().replace(",", ".")
        if not delay or not delay.isdigit():
            return

        self.log(f"Changing KeySender delay to {delay}...")
        self.update_aafk_settings(light_mode_delay=delay)

    def change_heavy_mode_delay(self, delay):
        """Changes the delay of the KeySender thread"""
        delay = delay.strip().replace(",", ".")
        if not delay or not delay.isdigit():
            return
        self.log(f"Changing KeySender delay to {delay}...")
        self.update_aafk_settings(heavy_mode_delay=delay)

    def change_heavy_mode_path(self, path):
        """Changes the path of the KeySender thread"""
        self.log(f"Changing KeySender path to {path}...")
        self.update_aafk_settings(heavy_mode_path=path)

    def toggle_console(self):
        """Opens the console if it's closed, closes it if it's open"""
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
        """Logs text to the console"""
        self.console.append(text)
        self.console.verticalScrollBar().setValue(
            self.console.verticalScrollBar().maximum())

    def start_anti_afk(self):
        window = find_window("VALORANT")
        assert window, "VALORANT window not found"

        self.log(f'Starting KeySender with mode "{self._anti_afk_mode}"...')
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

        self.aafk = KeySender(mode=self._anti_afk_mode, window_handle=window)
        self.anti_afk_status = True
        self.aafk.start()

    def stop_anti_afk(self):
        assert self.aafk, "KeySender not created"

        self.log("Killing KeySender...")
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.anti_afk_status = False
        self.aafk.stop()

    def close(self, a0: QCloseEvent):
        """Stops the KeySender thread when the window is closed"""
        if self.aafk:
            self.log("Killing KeySender...")
            self.stop_anti_afk()
        a0.accept()