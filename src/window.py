import win32gui
from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QKeySequence, QTextOption, QFont, QDoubleValidator, QCloseEvent
from PyQt6.QtWidgets import (QMainWindow, QSizePolicy, QPushButton, QLabel, QTextEdit, QVBoxLayout, QGroupBox, QComboBox,
                             QHBoxLayout, QLineEdit, QWidget)

from sender import Mode, KeySender
from mytypes import Handle


def find_window(window_name: str) -> Handle | None:
    """Returns the window handle if found, None if not"""
    def enum_windows_callback(hwnd, windows):
        if window_name in win32gui.GetWindowText(hwnd):
            windows.append(hwnd)
    
    windows = []
    win32gui.EnumWindows(enum_windows_callback, windows)
    return windows[0] if windows else None


class MainWindow(QMainWindow):
    class Status:
        NOT_WORKING = "<font color='red'>Not working</font>"
        WORKING = "<font color='green'>Working</font>"
        NOT_FOUND = "<font color='red'>Not found</font>"
        FOUND = "<font color='green'>Found</font>"

    def __init__(self, parent=None, flags=Qt.WindowType.Widget):
        super().__init__(parent, flags)

        self.setWindowTitle("Anti-AFK")
        self.setSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Minimum)
        self.setMinimumSize(self.minimumSizeHint())

        self.aafk = None
        self._anti_afk_status = False
        self._anti_afk_settings = {}
        self._anti_afk_mode = Mode.LIGHT
        self._console_open = False
        self._valorant_status = False

        self._init_ui()
        self._connect_signals()

        self.valorant_status_timer = QTimer(self)
        self.valorant_status_timer.timeout.connect(self.update_valorant_status)
        self.valorant_status_timer.start(5000)

    def _init_ui(self):
        self.start_button = self._create_button("Start", enabled=True)
        self.stop_button = self._create_button("Stop", enabled=False)
        self.console_button = self._create_button("Open console", style="color: gray;")
        
        self.status_label = QLabel(f"Status: {self.Status.NOT_WORKING}")
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.console = self._create_console()
        
        controls_layout = QVBoxLayout()
        controls_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        controls_layout.addWidget(self.start_button)
        controls_layout.addWidget(self.stop_button)
        controls_layout.addWidget(self.console_button)
        controls_layout.addWidget(self.status_label)
        controls_layout.addWidget(self.console)

        self.controls_group = QGroupBox("Controls")
        self.controls_group.setLayout(controls_layout)

        self.window_status_label = QLabel()
        self.update_valorant_status()

        mode_input = QComboBox()
        mode_input.addItems(KeySender.MODES_NAMES)
        mode_layout = QHBoxLayout()
        mode_layout.addWidget(QLabel("Mode:"))
        mode_layout.addWidget(mode_input)

        self.hint_label = QLabel()
        self.hint_label.setWordWrap(True)
        self.hint_label.setAlignment(Qt.AlignmentFlag.AlignLeft)

        self.light_mode_settings_group = self._create_light_mode_settings()
        self.heavy_mode_settings_group = self._create_heavy_mode_settings()
        self.heavy_mode_settings_group.hide()

        settings_layout = QVBoxLayout()
        settings_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        settings_layout.addWidget(self.window_status_label)
        settings_layout.addLayout(mode_layout)
        settings_layout.addWidget(self.hint_label)
        settings_layout.addWidget(self.light_mode_settings_group)
        settings_layout.addWidget(self.heavy_mode_settings_group)

        settings_group = QGroupBox("Settings")
        settings_group.setLayout(settings_layout)

        main_layout = QHBoxLayout()
        main_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        main_layout.addWidget(self.controls_group)
        main_layout.addWidget(settings_group)

        main_widget = QWidget()
        main_widget.setLayout(main_layout)
        self.setCentralWidget(main_widget)

    def _create_button(self, text, enabled=True, style=None):
        button = QPushButton(text)
        button.setEnabled(enabled)
        button.setShortcut(QKeySequence(""))
        button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        if style:
            button.setStyleSheet(style)
        return button

    def _create_console(self):
        console = QTextEdit('')
        console.setReadOnly(True)
        console.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        console.setWordWrapMode(QTextOption.WrapMode.NoWrap)
        console.setFont(QFont("Consolas", 10))
        console.setStyleSheet("background-color: black; color: white; border-radius: 5px;")
        console.hide()
        return console

    def _create_light_mode_settings(self):
        delay_input = QLineEdit()
        delay_input.setPlaceholderText("5.0")
        delay_input.setValidator(QDoubleValidator(0.0, 10, 4))

        layout = QHBoxLayout()
        layout.addWidget(QLabel("Space delay:"))
        layout.addWidget(delay_input)

        group = QGroupBox("Light mode settings")
        group.setLayout(layout)
        return group

    def _create_heavy_mode_settings(self):
        delay_input = QLineEdit()
        delay_input.setPlaceholderText("0.5")
        delay_input.setValidator(QDoubleValidator(0.0, 10, 4))

        path_input = QLineEdit()
        path_input.setPlaceholderText("WASD")

        layout = QVBoxLayout()
        layout.addWidget(QLabel("Key delay:"))
        layout.addWidget(delay_input)
        layout.addWidget(QLabel("Path:"))
        layout.addWidget(path_input)

        group = QGroupBox("Heavy mode settings")
        group.setLayout(layout)
        return group

    def _connect_signals(self):
        self.start_button.clicked.connect(self.start_anti_afk)
        self.stop_button.clicked.connect(self.stop_anti_afk)
        self.console_button.clicked.connect(self.toggle_console)

        mode_input = self.findChild(QComboBox)
        mode_input.currentTextChanged.connect(self.change_mode)

        light_delay_input = self.light_mode_settings_group.findChild(QLineEdit)
        light_delay_input.textChanged.connect(self.change_light_mode_delay)

        heavy_delay_input = self.heavy_mode_settings_group.findChildren(QLineEdit)[0]
        heavy_path_input = self.heavy_mode_settings_group.findChildren(QLineEdit)[1]
        heavy_delay_input.textChanged.connect(self.change_heavy_mode_delay)
        heavy_path_input.textChanged.connect(self.change_heavy_mode_path)

    def update_valorant_status(self):
        window = find_window("VALORANT")
        status = self.Status.FOUND if window else self.Status.NOT_FOUND
        self.window_status_label.setText(f"VALORANT: {status}")
        self.controls_group.setEnabled(bool(window))
        self._valorant_status = bool(window)

    @property
    def anti_afk_status(self):
        return self._anti_afk_status

    @anti_afk_status.setter
    def anti_afk_status(self, value):
        self.log(f"KeySender status: {value}")
        self._anti_afk_status = value
        status = self.Status.WORKING if value else self.Status.NOT_WORKING
        self.status_label.setText(f"Status: {status}")

    def change_mode(self, mode):
        self.log(f"Changing AntiAFK mode to {mode}...")
        self._anti_afk_mode = mode

        if mode == Mode.LIGHT.value:
            self.hint_label.setText("Sending <b>SPACE</b> key press to VALORANT window every <b>N</b> seconds")
            self.light_mode_settings_group.show()
            self.heavy_mode_settings_group.hide()
        elif mode == Mode.HEAVY.value:
            self.hint_label.setText("Sending sequence of keys to <b>VALORANT</b> like you are playing <i>(WASD + SPACE)</i> every <b>N</b> seconds")
            self.light_mode_settings_group.hide()
            self.heavy_mode_settings_group.show()

    def update_aafk_settings(self, **kwargs):
        self.log(f"Updating KeySender settings: {kwargs}...")
        self._anti_afk_settings.update(kwargs)
        if self.aafk:
            self.aafk.update_settings(self._anti_afk_settings)

    def change_light_mode_delay(self, delay):
        delay = delay.strip().replace(",", ".")
        if delay and delay.replace(".", "").isdigit():
            self.log(f"Changing KeySender delay to {delay}...")
            self.update_aafk_settings(light_mode_delay=float(delay))

    def change_heavy_mode_delay(self, delay):
        delay = delay.strip().replace(",", ".")
        if delay and delay.replace(".", "").isdigit():
            self.log(f"Changing KeySender delay to {delay}...")
            self.update_aafk_settings(heavy_mode_delay=float(delay))

    def change_heavy_mode_path(self, path):
        self.log(f"Changing KeySender path to {path}...")
        self.update_aafk_settings(heavy_mode_path=path)

    def toggle_console(self):
        self._console_open = not self._console_open
        self.console_button.setText("Close console" if self._console_open else "Open console")
        self.console.setVisible(self._console_open)

    def log(self, text):
        self.console.append(text)
        self.console.verticalScrollBar().setValue(self.console.verticalScrollBar().maximum())

    def start_anti_afk(self):
        window = find_window("VALORANT")
        if not window:
            self.log("VALORANT window not found")
            return

        self.log(f'Starting KeySender with mode "{self._anti_afk_mode}"...')
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

        self.aafk = KeySender(mode=self._anti_afk_mode, window_handle=window)
        self.anti_afk_status = True
        self.aafk.start()

    def stop_anti_afk(self):
        if not self.aafk:
            self.log("KeySender not created")
            return

        self.log("Stopping KeySender...")
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.anti_afk_status = False
        self.aafk.stop()

    def closeEvent(self, event: QCloseEvent):
        if self.aafk:
            self.log("Stopping KeySender...")
            self.stop_anti_afk()
        event.accept()