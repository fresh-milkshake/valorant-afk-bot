import win32gui
from PyQt6.QtCore import Qt, QTimer, QDateTime
from PyQt6.QtGui import (
    QKeySequence,
    QTextOption,
    QFont,
    QDoubleValidator,
    QCloseEvent,
    QIcon,
)
from PyQt6.QtWidgets import (
    QMainWindow,
    QSizePolicy,
    QPushButton,
    QLabel,
    QTextEdit,
    QVBoxLayout,
    QGroupBox,
    QComboBox,
    QHBoxLayout,
    QLineEdit,
    QWidget,
    QSlider,
    QCheckBox,
    QFrame,
)

from sender import Mode, KeySender, MovementPattern
from mytypes import Handle, LoggingLevel


def find_window(window_name: str) -> Handle | None:
    """Returns window handle if found, otherwise None"""

    def enum_windows_callback(hwnd, windows):
        if window_name in win32gui.GetWindowText(hwnd):
            windows.append(hwnd)

    windows = []
    win32gui.EnumWindows(enum_windows_callback, windows)
    return windows[0] if windows else None


class MainWindow(QMainWindow):
    class Status:
        NOT_WORKING = "<font color='#ff4444'>Inactive</font>"
        WORKING = "<font color='#44ff44'>Working</font>"
        NOT_FOUND = "<font color='#ff4444'>Not Found</font>"
        FOUND = "<font color='#44ff44'>Found</font>"

    def __init__(self, parent=None, flags=Qt.WindowType.Widget):
        super().__init__(parent, flags)

        self.setWindowTitle("Valorant AFK bot")
        self.setSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Minimum)
        self.setMinimumSize(500, 400)
        self.setStyleSheet("""
            QMainWindow {
                background-color: #2b2b2b;
            }
            QGroupBox {
                color: #ffffff;
                border: 1px solid #3d3d3d;
                border-radius: 8px;
                margin-top: 1ex;
                padding: 12px;
                background-color: #333333;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                left: 10px;
                padding: 0 5px;
                background-color: #333333;
                font-weight: bold;
            }
            QPushButton {
                background-color: #3d3d3d;
                color: white;
                border: none;
                padding: 10px 20px;
                border-radius: 6px;
                min-width: 120px;
                font-weight: bold;
            }
            QPushButton:hover {
                background-color: #4d4d4d;
                border: 1px solid #5d5d5d;
            }
            QPushButton:pressed {
                background-color: #353535;
            }
            QPushButton:disabled {
                background-color: #2d2d2d;
                color: #666666;
            }
            QLabel {
                color: #ffffff;
            }
            QLineEdit {
                background-color: #3d3d3d;
                color: white;
                border: 1px solid #4d4d4d;
                border-radius: 5px;
                padding: 6px;
            }
            QLineEdit:focus {
                border: 1px solid #6d6d6d;
            }
            QComboBox {
                background-color: #3d3d3d;
                color: white;
                border: 1px solid #4d4d4d;
                border-radius: 5px;
                padding: 6px;
                min-width: 120px;
            }
            QComboBox:hover {
                border: 1px solid #5d5d5d;
            }
            QComboBox::drop-down {
                border: none;
                width: 20px;
            }
            QComboBox::down-arrow {
                width: 14px;
                height: 14px;
                image: url(assets/down-arrow.png);
            }
            QTextEdit {
                background-color: #1e1e1e;
                color: #ffffff;
                border: 1px solid #3d3d3d;
                border-radius: 5px;
                padding: 8px;
                selection-background-color: #3d3d3d;
            }
            QSlider::groove:horizontal {
                border: 1px solid #3d3d3d;
                height: 6px;
                background: #2d2d2d;
                border-radius: 3px;
            }
            QSlider::handle:horizontal {
                background: #5d5d5d;
                border: 1px solid #4d4d4d;
                width: 16px;
                height: 16px;
                border-radius: 8px;
                margin: -5px 0;
            }
            QSlider::handle:horizontal:hover {
                background: #6d6d6d;
            }
            QCheckBox {
                color: #ffffff;
                spacing: 5px;
            }
            QCheckBox::indicator {
                width: 16px;
                height: 16px;
                border-radius: 3px;
                border: 1px solid #4d4d4d;
                background-color: #3d3d3d;
            }
            QCheckBox::indicator:checked {
                background-color: #5d5d5d;
                border: 1px solid #6d6d6d;
            }
        """)

        self.aafk = None
        self._anti_afk_status = False
        self._anti_afk_settings = {}
        self._anti_afk_mode = Mode.LIGHT
        self._console_open = False
        self._valorant_status = False
        self._advanced_settings_visible = False

        self._init_ui()
        self._connect_signals()

        self.valorant_status_timer = QTimer(self)
        self.valorant_status_timer.timeout.connect(self.update_valorant_status)
        self.valorant_status_timer.start(5000)

    def _init_ui(self):
        self.start_button = self._create_button("Start", enabled=True)
        self.stop_button = self._create_button("Stop", enabled=False)
        self.console_button = self._create_button(
            "Open Console", style="color: #cccccc;"
        )

        self.status_label = QLabel(f"Status: {self.Status.NOT_WORKING}")
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.status_label.setStyleSheet(
            "font-size: 14px; font-weight: bold; padding: 5px;"
        )

        self.console = self._create_console()

        controls_layout = QVBoxLayout()
        controls_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        controls_layout.setSpacing(12)
        controls_layout.addWidget(self.start_button)
        controls_layout.addWidget(self.stop_button)
        controls_layout.addWidget(self.console_button)
        controls_layout.addWidget(self.status_label)
        controls_layout.addStretch()
        controls_layout.addWidget(self.console, 1)

        self.controls_group = QGroupBox("Control")
        self.controls_group.setLayout(controls_layout)

        # Settings section
        self.window_status_label = QLabel()
        self.window_status_label.setStyleSheet(
            "font-size: 14px; font-weight: bold; padding: 5px;"
        )
        self.update_valorant_status()

        # Mode selection
        self.mode_input = QComboBox()
        self.mode_input.addItem("Jumping")
        self.mode_input.addItem("WASD")
        mode_layout = QHBoxLayout()
        mode_layout.addWidget(QLabel("Working mode:"))
        mode_layout.addWidget(self.mode_input)
        mode_layout.addStretch()

        # Mode hint
        self.hint_label = QLabel()
        self.hint_label.setWordWrap(True)
        self.hint_label.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.hint_label.setStyleSheet(
            "color: #aaaaaa; font-style: italic; padding: 5px; font-size: 11px;"
        )

        # Basic settings
        self.light_mode_settings_group = self._create_light_mode_settings()
        self.heavy_mode_settings_group = self._create_heavy_mode_settings()
        self.heavy_mode_settings_group.hide()

        # Advanced settings toggle
        self.advanced_toggle = QPushButton("Show Advanced Settings")
        self.advanced_toggle.setStyleSheet("QPushButton { min-width: 160px; font-size: 11px; }")
        self.advanced_toggle.hide()
        
        # Advanced settings (initially hidden)
        self.advanced_settings_group = self._create_advanced_settings()
        self.advanced_settings_group.hide()

        settings_layout = QVBoxLayout()
        settings_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        settings_layout.setSpacing(12)
        settings_layout.addWidget(self.window_status_label)
        settings_layout.addLayout(mode_layout)
        settings_layout.addWidget(self.hint_label)
        settings_layout.addWidget(self.light_mode_settings_group)
        settings_layout.addWidget(self.heavy_mode_settings_group)
        settings_layout.addWidget(self.advanced_toggle)
        settings_layout.addWidget(self.advanced_settings_group)
        settings_layout.addStretch()

        settings_group = QGroupBox("Settings")
        settings_group.setLayout(settings_layout)

        main_layout = QHBoxLayout()
        main_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        main_layout.setSpacing(20)
        main_layout.addWidget(self.controls_group)
        main_layout.addWidget(settings_group)

        main_widget = QWidget()
        main_widget.setLayout(main_layout)
        self.setCentralWidget(main_widget)

        # Set mode hints
        self._update_mode_hint(Mode.LIGHT)

    def _create_button(self, text, enabled=True, style=None):
        button = QPushButton(text)
        button.setEnabled(enabled)
        button.setShortcut(QKeySequence(""))
        button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        if style:
            button.setStyleSheet(button.styleSheet() + style)
        return button

    def _create_console(self):
        console = QTextEdit("")
        console.setReadOnly(True)
        console.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        console.setWordWrapMode(QTextOption.WrapMode.NoWrap)
        console.setFont(QFont("Consolas", 10))
        console.setStyleSheet("""
            QTextEdit {
                background-color: #1a1a1a;
                color: #ffffff;
                border: 1px solid #3d3d3d;
                border-radius: 5px;
                padding: 10px;
            }
            QScrollBar:vertical {
                border: none;
                background: #2d2d2d;
                width: 12px;
                margin: 0px;
            }
            QScrollBar::handle:vertical {
                background: #4d4d4d;
                min-height: 20px;
                border-radius: 3px;
            }
            QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
                height: 0px;
            }
        """)
        console.hide()
        return console

    def _create_light_mode_settings(self):
        delay_input = QLineEdit()
        delay_input.setPlaceholderText("5.0")
        delay_input.setValidator(QDoubleValidator(0.1, 60.0, 2))
        delay_input.setMinimumWidth(100)
        delay_input.setToolTip("Delay between jumps in seconds (0.1-60.0)")

        layout = QHBoxLayout()
        layout.addWidget(QLabel("Jump delay (sec):"))
        layout.addWidget(delay_input)
        layout.addStretch()

        group = QGroupBox("Jumping Mode Settings")
        group.setLayout(layout)
        return group

    def _create_heavy_mode_settings(self):
        delay_input = QLineEdit()
        delay_input.setPlaceholderText("0.5")
        delay_input.setValidator(QDoubleValidator(0.1, 5.0, 2))
        delay_input.setMinimumWidth(100)
        delay_input.setToolTip("Duration of each key press in seconds (0.1-5.0)")

        path_input = QLineEdit()
        path_input.setPlaceholderText("WASD")
        path_input.setMinimumWidth(100)
        path_input.setToolTip("Custom movement keys (e.g., WASD, WS, AD, etc.)")

        info_label = QLabel("Basic movement settings. Use Advanced Settings for more control.")
        info_label.setWordWrap(True)
        info_label.setStyleSheet("color: #aaaaaa; font-style: italic; font-size: 10px;")

        layout = QVBoxLayout()
        delay_layout = QHBoxLayout()
        delay_layout.addWidget(QLabel("Key press delay (sec):"))
        delay_layout.addWidget(delay_input)
        delay_layout.addStretch()

        path_layout = QHBoxLayout()
        path_layout.addWidget(QLabel("Movement path:"))
        path_layout.addWidget(path_input)
        path_layout.addStretch()

        layout.addLayout(delay_layout)
        layout.addLayout(path_layout)
        layout.addWidget(info_label)

        group = QGroupBox("WASD Mode Settings")
        group.setLayout(layout)
        return group

    def _create_advanced_settings(self):
        layout = QVBoxLayout()
        
        # Movement Pattern
        pattern_layout = QHBoxLayout()
        self.pattern_combo = QComboBox()
        self.pattern_combo.addItems(["Random", "Circle", "Strafe", "Forward/Back", "Custom"])
        self.pattern_combo.setToolTip("Movement pattern type")
        pattern_layout.addWidget(QLabel("Pattern:"))
        pattern_layout.addWidget(self.pattern_combo)
        pattern_layout.addStretch()
        
        # Sliders for various settings
        self.intensity_slider = self._create_slider(0.1, 1.0, 0.7, "Movement Intensity", 
                                                   "How active the movement is")
        self.frequency_slider = self._create_slider(0.1, 1.0, 0.3, "Direction Change Freq", 
                                                   "How often to change direction")
        self.action_prob_slider = self._create_slider(0.0, 1.0, 0.4, "Action Probability", 
                                                     "Probability of additional actions")
        self.strafe_pref_slider = self._create_slider(0.0, 1.0, 0.5, "Strafe Preference", 
                                                     "Preference for strafing vs forward/back")
        self.smoothness_slider = self._create_slider(0.1, 1.0, 0.6, "Movement Smoothness", 
                                                    "How smooth transitions are")
        self.pause_freq_slider = self._create_slider(0.0, 1.0, 0.2, "Pause Frequency", 
                                                    "How often to pause movement")
        
        layout.addLayout(pattern_layout)
        layout.addLayout(self.intensity_slider["layout"])
        layout.addLayout(self.frequency_slider["layout"])
        layout.addLayout(self.action_prob_slider["layout"])
        layout.addLayout(self.strafe_pref_slider["layout"])
        layout.addLayout(self.smoothness_slider["layout"])
        layout.addLayout(self.pause_freq_slider["layout"])

        group = QGroupBox("Advanced WASD Settings")
        group.setLayout(layout)
        return group

    def _create_slider(self, min_val, max_val, default_val, label_text, tooltip):
        layout = QHBoxLayout()
        
        label = QLabel(f"{label_text}:")
        label.setMinimumWidth(120)
        label.setToolTip(tooltip)
        
        slider = QSlider(Qt.Orientation.Horizontal)
        slider.setMinimum(int(min_val * 100))
        slider.setMaximum(int(max_val * 100))
        slider.setValue(int(default_val * 100))
        slider.setToolTip(tooltip)
        
        value_label = QLabel(f"{default_val:.2f}")
        value_label.setMinimumWidth(40)
        value_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        
        layout.addWidget(label)
        layout.addWidget(slider)
        layout.addWidget(value_label)
        
        # Connect slider to update value label
        slider.valueChanged.connect(lambda v: value_label.setText(f"{v/100:.2f}"))
        
        return {
            "layout": layout,
            "slider": slider,
            "label": value_label
        }

    def _connect_signals(self):
        self.start_button.clicked.connect(self.start_anti_afk)
        self.stop_button.clicked.connect(self.stop_anti_afk)
        self.console_button.clicked.connect(self.toggle_console)
        self.advanced_toggle.clicked.connect(self.toggle_advanced_settings)

        # Mode selection
        self.mode_input.currentIndexChanged.connect(
            lambda index: self.change_mode(Mode.LIGHT if index == 0 else Mode.HEAVY)
        )

        # Basic settings - Light mode
        delay_input = self.light_mode_settings_group.findChild(QLineEdit)
        delay_input.textChanged.connect(self.change_light_mode_delay)

        # Basic settings - Heavy mode
        heavy_mode_inputs = self.heavy_mode_settings_group.findChildren(QLineEdit)
        heavy_mode_inputs[0].textChanged.connect(self.change_heavy_mode_delay)
        heavy_mode_inputs[1].textChanged.connect(self.change_heavy_mode_path)

        # Advanced settings
        self.pattern_combo.currentTextChanged.connect(self.change_pattern_type)
        self.intensity_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(movement_intensity=v/100)
        )
        self.frequency_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(direction_change_frequency=v/100)
        )
        self.action_prob_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(action_probability=v/100)
        )
        self.strafe_pref_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(strafe_preference=v/100)
        )
        self.smoothness_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(movement_smoothness=v/100)
        )
        self.pause_freq_slider["slider"].valueChanged.connect(
            lambda v: self.update_aafk_settings(pause_frequency=v/100)
        )

    def toggle_advanced_settings(self):
        self._advanced_settings_visible = not self._advanced_settings_visible
        if self._advanced_settings_visible:
            self.advanced_settings_group.show()
            self.advanced_toggle.setText("Hide Advanced Settings")
            self.setMinimumSize(500, 650)
        else:
            self.advanced_settings_group.hide()
            self.advanced_toggle.setText("Show Advanced Settings")
            self.setMinimumSize(500, 400)

    def update_valorant_status(self):
        valorant_hwnd = find_window("VALORANT")
        self._valorant_status = valorant_hwnd is not None
        self.window_status_label.setText(
            f"Valorant: {self.Status.FOUND if self._valorant_status else self.Status.NOT_FOUND}"
        )
        self.window_status_label.setStyleSheet(
            "font-size: 14px; font-weight: bold; padding: 5px;"
        )

    @property
    def anti_afk_status(self):
        return self._anti_afk_status

    @anti_afk_status.setter
    def anti_afk_status(self, value):
        self._anti_afk_status = value
        self.status_label.setText(
            f"Status: {self.Status.WORKING if value else self.Status.NOT_WORKING}"
        )
        self.start_button.setEnabled(not value)
        self.stop_button.setEnabled(value)

    def change_mode(self, mode):
        self._anti_afk_mode = mode
        if mode == Mode.LIGHT:
            self.light_mode_settings_group.show()
            self.heavy_mode_settings_group.hide()
            self.advanced_toggle.hide()
            self.advanced_settings_group.hide()
            self._advanced_settings_visible = False
        else:
            self.light_mode_settings_group.hide()
            self.heavy_mode_settings_group.show()
            self.advanced_toggle.show()

        self._update_mode_hint(mode)

    def change_pattern_type(self, pattern_text):
        pattern_map = {
            "Random": "random",
            "Circle": "circle", 
            "Strafe": "strafe",
            "Forward/Back": "forward_back",
            "Custom": "custom"
        }
        pattern_value = pattern_map.get(pattern_text, "random")
        self.update_aafk_settings(pattern_type=pattern_value)

    def _update_mode_hint(self, mode):
        if mode == Mode.LIGHT:
            self.hint_label.setText(
                "Simple mode: Random jumps with customizable delay. Perfect for basic AFK prevention."
            )
        else:
            self.hint_label.setText(
                "Advanced mode: Realistic movement simulation with multiple patterns and behaviors. "
                "Use Advanced Settings for detailed customization."
            )

    def update_aafk_settings(self, **kwargs):
        self._anti_afk_settings.update(kwargs)
        if self.aafk:
            self.aafk.update_settings(self._anti_afk_settings)
            self.log(f"Settings updated: {kwargs}", LoggingLevel.INFO)

    def change_light_mode_delay(self, delay):
        if delay:
            try:
                delay_val = float(delay)
                if 0.1 <= delay_val <= 60.0:
                    self.update_aafk_settings(light_mode_delay=delay)
                    self.log(f"Jump delay changed to {delay} sec", LoggingLevel.INFO)
            except ValueError:
                pass

    def change_heavy_mode_delay(self, delay):
        if delay:
            try:
                delay_val = float(delay)
                if 0.1 <= delay_val <= 5.0:
                    self.update_aafk_settings(heavy_mode_delay=delay)
                    self.log(f"Key press delay changed to {delay} sec", LoggingLevel.INFO)
            except ValueError:
                pass

    def change_heavy_mode_path(self, path):
        if path:
            # Validate path contains only WASD
            valid_chars = set("WASDwasd")
            if all(c in valid_chars for c in path):
                self.update_aafk_settings(heavy_mode_path=path.upper())
                self.log(f"Movement path changed to '{path.upper()}'", LoggingLevel.INFO)

    def toggle_console(self):
        self._console_open = not self._console_open
        if self._console_open:
            self.console.show()
            self.console_button.setText("Hide Logs")
        else:
            self.console.hide()
            self.console_button.setText("Show Logs")

    def log(self, text, level: LoggingLevel = LoggingLevel.INFO):
        """Enhanced logging with timestamp and color coding based on logging level"""
        timestamp = QDateTime.currentDateTime().toString("HH:mm:ss")

        # Color mapping based on logging level
        color_map = {
            LoggingLevel.DEBUG: "#cccccc",    # light gray for debug
            LoggingLevel.INFO: "#ffffff",     # white for info
            LoggingLevel.WARNING: "#ffcc66",  # orange for warnings
            LoggingLevel.ERROR: "#ff6b6b"     # red for errors
        }
        
        color = color_map.get(level, "#ffffff")
        level_text = f"[{level.value}]"

        log_entry = f"<span style='color: #999999;'>[{timestamp}] {level_text}</span> <span style='color: {color};'>{text}</span>"

        # Scroll to bottom only if we were already at the bottom
        scrollbar = self.console.verticalScrollBar()
        at_bottom = scrollbar.value() == scrollbar.maximum()

        self.console.append(log_entry)

        if at_bottom:
            scrollbar.setValue(scrollbar.maximum())

    def start_anti_afk(self):
        valorant_hwnd = find_window("VALORANT")
        if not valorant_hwnd:
            self.log("VALORANT not found!", LoggingLevel.ERROR)
            return

        if self.aafk and self.aafk.running:
            self.log("Anti-AFK already started", LoggingLevel.WARNING)
            return

        try:
            self.aafk = KeySender(self._anti_afk_mode, valorant_hwnd)
            self.aafk.update_settings(self._anti_afk_settings)
            self.aafk.start()
            self.anti_afk_status = True
            self.log(f"Anti-AFK started in mode: {self._anti_afk_mode.value}", LoggingLevel.INFO)
        except Exception as e:
            self.log(f"Error during startup: {str(e)}", LoggingLevel.ERROR)

    def stop_anti_afk(self):
        if self.aafk:
            self.aafk.stop()
            self.log("Anti-AFK stopped", LoggingLevel.INFO)

        self.anti_afk_status = False

    def closeEvent(self, event: QCloseEvent):
        """When closing the application, stop all threads"""
        self.stop_anti_afk()
        self.log("Application closing...", LoggingLevel.INFO)
        # Give threads some time to finish
        if self.aafk:
            self.aafk.join(1.0)
        super().closeEvent(event)
