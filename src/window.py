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
)

from sender import Mode, KeySender
from mytypes import Handle


def find_window(window_name: str) -> Handle | None:
    """Возвращает хэндл окна если найдено, иначе None"""

    def enum_windows_callback(hwnd, windows):
        if window_name in win32gui.GetWindowText(hwnd):
            windows.append(hwnd)

    windows = []
    win32gui.EnumWindows(enum_windows_callback, windows)
    return windows[0] if windows else None


class MainWindow(QMainWindow):
    class Status:
        NOT_WORKING = "<font color='#ff4444'>Не активно</font>"
        WORKING = "<font color='#44ff44'>Работает</font>"
        NOT_FOUND = "<font color='#ff4444'>Не найдено</font>"
        FOUND = "<font color='#44ff44'>Найдено</font>"

    def __init__(self, parent=None, flags=Qt.WindowType.Widget):
        super().__init__(parent, flags)

        self.setWindowTitle("Valorant Анти-AFK")
        self.setSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Minimum)
        self.setMinimumSize(450, 350)
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
        """)

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
        self.start_button = self._create_button("Запустить", enabled=True)
        self.stop_button = self._create_button("Остановить", enabled=False)
        self.console_button = self._create_button(
            "Открыть консоль", style="color: #cccccc;"
        )

        self.status_label = QLabel(f"Статус: {self.Status.NOT_WORKING}")
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
        controls_layout.addWidget(self.console)
        controls_layout.addStretch()

        self.controls_group = QGroupBox("Управление")
        self.controls_group.setLayout(controls_layout)

        self.window_status_label = QLabel()
        self.window_status_label.setStyleSheet(
            "font-size: 14px; font-weight: bold; padding: 5px;"
        )
        self.update_valorant_status()

        mode_input = QComboBox()
        mode_input.addItem("Прыжки")
        mode_input.addItem("WASD")
        mode_layout = QHBoxLayout()
        mode_layout.addWidget(QLabel("Режим работы:"))
        mode_layout.addWidget(mode_input)
        mode_layout.addStretch()

        self.hint_label = QLabel()
        self.hint_label.setWordWrap(True)
        self.hint_label.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.hint_label.setStyleSheet(
            "color: #aaaaaa; font-style: italic; padding: 5px;"
        )

        self.light_mode_settings_group = self._create_light_mode_settings()
        self.heavy_mode_settings_group = self._create_heavy_mode_settings()
        self.heavy_mode_settings_group.hide()

        settings_layout = QVBoxLayout()
        settings_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        settings_layout.setSpacing(12)
        settings_layout.addWidget(self.window_status_label)
        settings_layout.addLayout(mode_layout)
        settings_layout.addWidget(self.hint_label)
        settings_layout.addWidget(self.light_mode_settings_group)
        settings_layout.addWidget(self.heavy_mode_settings_group)
        settings_layout.addStretch()

        settings_group = QGroupBox("Настройки")
        settings_group.setLayout(settings_layout)

        main_layout = QHBoxLayout()
        main_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        main_layout.setSpacing(20)
        main_layout.addWidget(self.controls_group)
        main_layout.addWidget(settings_group)

        main_widget = QWidget()
        main_widget.setLayout(main_layout)
        self.setCentralWidget(main_widget)

        # Установите подсказки для режимов
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
        delay_input.setValidator(QDoubleValidator(0.0, 10, 4))
        delay_input.setMinimumWidth(100)

        layout = QHBoxLayout()
        layout.addWidget(QLabel("Задержка прыжка (сек):"))
        layout.addWidget(delay_input)
        layout.addStretch()

        group = QGroupBox("Настройки режима прыжков")
        group.setLayout(layout)
        return group

    def _create_heavy_mode_settings(self):
        delay_input = QLineEdit()
        delay_input.setPlaceholderText("0.5")
        delay_input.setValidator(QDoubleValidator(0.0, 10, 4))
        delay_input.setMinimumWidth(100)

        path_input = QLineEdit()
        path_input.setPlaceholderText("WASD")
        path_input.setMinimumWidth(100)

        info_label = QLabel("Укажите клавиши для перемещения (например, WASD, WS, AD и т.д.)")
        info_label.setWordWrap(True)
        info_label.setStyleSheet("color: #aaaaaa; font-style: italic; font-size: 10px;")

        layout = QVBoxLayout()
        delay_layout = QHBoxLayout()
        delay_layout.addWidget(QLabel("Задержка нажатия (сек):"))
        delay_layout.addWidget(delay_input)
        delay_layout.addStretch()

        path_layout = QHBoxLayout()
        path_layout.addWidget(QLabel("Путь перемещения:"))
        path_layout.addWidget(path_input)
        path_layout.addStretch()

        layout.addLayout(delay_layout)
        layout.addLayout(path_layout)
        layout.addWidget(info_label)

        group = QGroupBox("Настройки режима WASD")
        group.setLayout(layout)
        return group

    def _connect_signals(self):
        self.start_button.clicked.connect(self.start_anti_afk)
        self.stop_button.clicked.connect(self.stop_anti_afk)
        self.console_button.clicked.connect(self.toggle_console)

        # Получаем доступ к комбобоксу режима работы
        mode_combobox = self.findChild(QComboBox)
        mode_combobox.currentIndexChanged.connect(
            lambda index: self.change_mode(Mode.LIGHT if index == 0 else Mode.HEAVY)
        )

        # Получаем доступ к настройкам легкого режима
        delay_input = self.light_mode_settings_group.findChild(QLineEdit)
        delay_input.textChanged.connect(self.change_light_mode_delay)

        # Получаем доступ к настройкам тяжелого режима
        heavy_mode_inputs = self.heavy_mode_settings_group.findChildren(QLineEdit)
        heavy_mode_inputs[0].textChanged.connect(self.change_heavy_mode_delay)
        heavy_mode_inputs[1].textChanged.connect(self.change_heavy_mode_path)

    def update_valorant_status(self):
        valorant_hwnd = find_window("VALORANT")
        self._valorant_status = valorant_hwnd is not None
        self.window_status_label.setText(
            f"Valorant: {self.Status.FOUND if self._valorant_status else self.Status.NOT_FOUND}"
        )
        self.window_status_label.setStyleSheet(
            f"font-size: 14px; font-weight: bold; padding: 5px;"
        )

    @property
    def anti_afk_status(self):
        return self._anti_afk_status

    @anti_afk_status.setter
    def anti_afk_status(self, value):
        self._anti_afk_status = value
        self.status_label.setText(
            f"Статус: {self.Status.WORKING if value else self.Status.NOT_WORKING}"
        )
        self.start_button.setEnabled(not value)
        self.stop_button.setEnabled(value)

    def change_mode(self, mode):
        self._anti_afk_mode = mode
        if mode == Mode.LIGHT:
            self.light_mode_settings_group.show()
            self.heavy_mode_settings_group.hide()
        else:
            self.light_mode_settings_group.hide()
            self.heavy_mode_settings_group.show()

        self._update_mode_hint(mode)

    def _update_mode_hint(self, mode):
        if mode == Mode.LIGHT:
            self.hint_label.setText(
                "Режим прыжков: программа имитирует случайные прыжки с заданной задержкой. "
            )
        else:
            self.hint_label.setText(
                "Режим WASD: имитация активного перемещения в разные стороны с периодическими "
                "прыжками и нажатием Ctrl. "
            )

    def update_aafk_settings(self, **kwargs):
        self._anti_afk_settings.update(kwargs)
        if self.aafk:
            self.aafk.update_settings(self._anti_afk_settings)
            self.log(f"Настройки обновлены: {kwargs}")

    def change_light_mode_delay(self, delay):
        if delay:
            self.update_aafk_settings(light_mode_delay=delay)
            self.log(f"Задержка легкого режима изменена на {delay} сек")

    def change_heavy_mode_delay(self, delay):
        if delay:
            self.update_aafk_settings(heavy_mode_delay=delay)
            self.log(f"Задержка тяжелого режима изменена на {delay} сек")

    def change_heavy_mode_path(self, path):
        if path:
            self.update_aafk_settings(heavy_mode_path=path)
            self.log(f"Путь перемещения изменен на '{path}'")

    def toggle_console(self):
        self._console_open = not self._console_open
        if self._console_open:
            self.console.show()
            self.console_button.setText("Скрыть логи")
        else:
            self.console.hide()
            self.console_button.setText("Открыть логи")

    def log(self, text):
        """Улучшенное логирование с отметкой времени и цветовым кодированием"""
        timestamp = QDateTime.currentDateTime().toString("HH:mm:ss")

        if "ошибка" in text.lower() or "не найдено" in text.lower():
            color = "#ff6b6b"  # красный для ошибок
        elif "запущен" in text.lower() or "найден" in text.lower():
            color = "#69ff69"  # зеленый для успешных действий
        elif "изменена" in text.lower() or "обновлены" in text.lower():
            color = "#6bcfff"  # синий для обновлений
        elif "остановлен" in text.lower():
            color = "#ffcc66"  # оранжевый для предупреждений/информации
        else:
            color = "#ffffff"  # белый для обычных сообщений

        log_entry = f"<span style='color: #999999;'>[{timestamp}]</span> <span style='color: {color};'>{text}</span>"

        # Scroll to bottom only if we were already at the bottom
        scrollbar = self.console.verticalScrollBar()
        at_bottom = scrollbar.value() == scrollbar.maximum()

        self.console.append(log_entry)

        if at_bottom:
            scrollbar.setValue(scrollbar.maximum())

    def start_anti_afk(self):
        valorant_hwnd = find_window("VALORANT")
        if not valorant_hwnd:
            self.log("Ошибка: Игра VALORANT не найдена!")
            return

        if self.aafk and self.aafk.running:
            self.log("Анти-AFK уже запущен")
            return

        try:
            self.aafk = KeySender(self._anti_afk_mode, valorant_hwnd)
            self.aafk.update_settings(self._anti_afk_settings)
            self.aafk.start()
            self.anti_afk_status = True
            self.log(f"Анти-AFK запущен в режиме: {self._anti_afk_mode.value}")
        except Exception as e:
            self.log(f"Ошибка при запуске: {str(e)}")

    def stop_anti_afk(self):
        if self.aafk:
            self.aafk.stop()
            self.log("Анти-AFK остановлен")

        self.anti_afk_status = False

    def closeEvent(self, event: QCloseEvent):
        """При закрытии приложения останавливаем все потоки"""
        self.stop_anti_afk()
        self.log("Приложение закрывается...")
        # Дадим немного времени потокам на завершение
        if self.aafk:
            self.aafk.join(1.0)
        super().closeEvent(event)
