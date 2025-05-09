import random
import threading
from enum import Enum
import time

import win32api
import win32con
import win32gui

from time import sleep as wait

from mytypes import Hexadecimal, Handle


class Keys:
    """
    Bindings for the keys and their hexadecimal values
    """

    W = 0x57
    A = 0x41
    S = 0x53
    D = 0x44
    SPACE = 0x20
    SHIFT = 0x10
    CTRL = 0x11


class Mode(Enum):
    """
    Enum for the KeySender working modes

    LIGHT - Just jumps with random delay between jumps and rare other key presses
    HEAVY - Moves along a random path and rarely does random jumps
    """

    LIGHT = "Прыжки"
    HEAVY = "WASD"


class KeySender(threading.Thread):
    """
    Main class, implementing Thread class's run and stop methods for sending
    key presses to the game separated from the GUI thread
    """

    AVAILABLE_MODES = list(Mode)
    MODES_NAMES = [mode.value for mode in AVAILABLE_MODES]
    DEFAULT_PRESS_DURATION = 0.1
    DEFAULT_LIGHT_DELAY = 5.0
    DEFAULT_HEAVY_DELAY = 0.5
    DEFAULT_PATH = "WASD"

    def __init__(
        self,
        mode: Mode,
        window_handle: Handle,
        press_duration: float = DEFAULT_PRESS_DURATION,
    ):
        """
        Creates a new KeySender thread
        :param mode: The mode of the KeySender thread
        :param window_handle: The window handle of the game
        :param press_duration: Duration of key press in seconds
        """

        if not isinstance(mode, Mode):
            mode = Mode(mode)

        if mode not in self.AVAILABLE_MODES:
            raise ValueError(f"Неверный режим: {mode}")

        if not win32gui.IsWindow(window_handle):
            raise ValueError(f"Неверный дескриптор окна: {window_handle}")

        self.mode = mode
        self.valorant_hwnd = window_handle
        self._press_duration = press_duration
        self._last_window_check = 0
        self._window_check_interval = 10.0  # проверять окно каждые 10 секунд

        super().__init__()
        self.daemon = True  # делаем поток демоном, чтобы он завершался с основным
        self.running = False

        self._jump_delay = self.DEFAULT_LIGHT_DELAY
        self._last_action_time = time.time()
        
        # Расширенный набор случайных действий
        self._random_actions = [
            (Keys.SHIFT, 0.2),    # Бег
            (Keys.CTRL, 0.15),    # Присед
            (Keys.SPACE, 0.1),    # Прыжок
        ]
        
        # Случайные комбинации клавиш для имитации действий игрока
        self._action_combos = [
            [(Keys.W, 0.1), (Keys.SPACE, 0.1)],                   # Бег и прыжок
            [(Keys.CTRL, 0.15), (Keys.W, 0.1)],                   # Присесть и идти
            [(Keys.SHIFT, 0.2), (Keys.W, 0.15)],   # Бег вперед
            [(Keys.A, 0.1), (Keys.D, 0.1)],                       # Стрейф влево-вправо
        ]

        if self.mode == Mode.HEAVY:
            self.path = None
            self._wasd_sequence = []
            self._key_press_time = self.DEFAULT_HEAVY_DELAY
            self._movement_path = self.DEFAULT_PATH
            self._combo_chance = 0.6  # Шанс выполнить комбинацию клавиш
            self._movement_pattern_count = 0  # Счетчик паттернов движения
            self._max_movement_patterns = random.randint(3, 7)  # Макс. паттернов до смены

    @property
    def _jump_delay_diff(self):
        """Returns the 1/4 of the jump delay"""
        return self._jump_delay / 4

    def update_settings(self, settings: dict):
        """Updates the settings of the KeySender thread"""
        if self.mode == Mode.LIGHT:
            self._jump_delay = float(settings.get("light_mode_delay", self._jump_delay))
            return

        self._movement_path = settings.get("heavy_mode_path", self._movement_path)
        self._key_press_time = float(
            settings.get("heavy_mode_delay", self._key_press_time)
        )
        
        # Сбрасываем счетчики для применения новых настроек
        self._movement_pattern_count = 0
        self._max_movement_patterns = random.randint(3, 7)

    @property
    def jump_delay(self):
        """Returns a random delay between the jump delay +- jump delay difference"""
        variation = self._jump_delay * 0.4  # 40% вариации
        return random.uniform(
            self._jump_delay - variation,
            self._jump_delay + variation,
        )

    def is_window_active(self):
        current_time = time.time()
        if current_time - self._last_window_check >= self._window_check_interval:
            self._last_window_check = current_time
            return win32gui.IsWindow(self.valorant_hwnd) and win32gui.IsWindowVisible(self.valorant_hwnd)
        return True

    def send_key(self, key: Hexadecimal, delay: float):
        """Sends a key to the game"""
        if not self.is_window_active():
            self.running = False
            return
            
        # Небольшая случайная вариация в длительности нажатия
        actual_delay = delay * random.uniform(0.85, 1.15)
        
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(actual_delay)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def perform_random_action(self):
        """Performs a random action with a small chance"""
        if random.random() < 0.2:  # 20% шанс
            key, duration = random.choice(self._random_actions)
            self.send_key(key, duration)
            
            # Иногда добавляем второе случайное действие сразу после первого
            if random.random() < 0.3:  # 30% шанс второго действия
                wait(random.uniform(0.05, 0.2))
                second_key, second_duration = random.choice(self._random_actions)
                if second_key != key:  # Избегаем повторения той же клавиши
                    self.send_key(second_key, second_duration)

    def perform_action_combo(self):
        if random.random() < self._combo_chance:
            combo = random.choice(self._action_combos)
            for key, duration in combo:
                if not self.running:
                    break
                self.send_key(key, duration)
                wait(random.uniform(0.05, 0.15))
            return True
        return False

    def light_mode(self):
        """
        Runs the light mode - just jumps with random delay between jumps and
        rare other key presses and mouse movements
        """
        action_variance_counter = 0
        combo_counter = 0
        
        while self.running:
            current_time = time.time()
            if current_time - self._last_action_time >= self.jump_delay:
                # С некоторой вероятностью выполняем комбо вместо обычного прыжка
                if combo_counter >= 3 and random.random() < 0.4:
                    # Выполнить комбо действий
                    performed_combo = self.perform_action_combo()
                    if performed_combo:
                        combo_counter = 0
                else:
                    # Обычное действие - прыжок с вариациями
                    self.send_key(Keys.SPACE, self._press_duration * random.uniform(0.8, 1.2))
                    combo_counter += 1
                
                # Выполнение случайного действия
                self.perform_random_action()
                
                # Иногда делаем более сложную последовательность
                action_variance_counter += 1
                if action_variance_counter >= random.randint(3, 6) and random.random() < 0.35:
                    # Эмуляция проверки снаряжения или других действий
                    action_variance_counter = 0
                    sequence = []
                    
                    # Случайная последовательность 2-4 клавиш
                    for _ in range(random.randint(2, 4)):
                        key, duration = random.choice(self._random_actions)
                        sequence.append((key, duration))
                    
                    # Выполняем последовательность с разными задержками
                    for key, duration in sequence:
                        if not self.running:
                            break
                        self.send_key(key, duration)
                        # Более естественные паузы между нажатиями
                        wait(random.uniform(0.05, 0.25))
                
                self._last_action_time = current_time
            
            # Динамическая задержка для снижения нагрузки ЦП
            wait(random.uniform(0.08, 0.15))

    def heavy_mode(self):
        """
        Runs the heavy mode - moves along a random path and rarely does random
        jumps and mouse movements
        """
        while self.running:
            # Проверяем, нужно ли обновить паттерн движения
            self._movement_pattern_count += 1
            if self._movement_pattern_count > self._max_movement_patterns:
                # Изменяем максимальное количество повторений паттерна
                self._max_movement_patterns = random.randint(3, 7)
                self._movement_pattern_count = 0
                # Случайно решаем, будем ли использовать заданный путь или сгенерировать новый паттерн
                if random.random() < 0.3:  # 30% шанс на случайный паттерн
                    temp_path = ""
                    for _ in range(random.randint(3, 6)):
                        temp_path += random.choice("WASD")
                    # Временно используем случайный паттерн, не меняя сохраненные настройки
                    current_path = temp_path
                else:
                    current_path = self._movement_path
            else:
                current_path = self._movement_path
            
            # Преобразуем путь движения в последовательность клавиш
            key_sequence = []
            for char in current_path.upper():
                if char == "W":
                    # Случайно варьируем время удержания клавиши
                    key_sequence.append((Keys.W, self._key_press_time * random.uniform(0.75, 1.25)))
                elif char == "A":
                    key_sequence.append((Keys.A, self._key_press_time * random.uniform(0.75, 1.25)))
                elif char == "S":
                    key_sequence.append((Keys.S, self._key_press_time * random.uniform(0.75, 1.25)))
                elif char == "D":
                    key_sequence.append((Keys.D, self._key_press_time * random.uniform(0.75, 1.25)))
            
            # Добавляем прыжок и другие действия с различной вероятностью
            if random.random() < 0.45:  # 45% шанс добавить прыжок
                key_sequence.append((Keys.SPACE, self._press_duration * random.uniform(0.9, 1.1)))
            
            if random.random() < 0.25:  # 25% шанс добавить бег
                key_sequence.append((Keys.SHIFT, self._press_duration * 2 * random.uniform(0.9, 1.2)))
            
            if random.random() < 0.15:  # 15% шанс добавить присед
                key_sequence.append((Keys.CTRL, self._press_duration * 1.5 * random.uniform(0.9, 1.1)))
            
            # С определенной вероятностью перемешиваем последовательность
            # или сохраняем порядок для более естественного движения
            if random.random() < 0.6:  # 60% шанс перемешать
                random.shuffle(key_sequence)
            
            # Выполняем последовательность
            for key, duration in key_sequence:
                if not self.running:
                    break
                self.send_key(key, duration)
                
                # Шанс на дополнительное действие
                self.perform_random_action()
                
                # Случайная пауза между клавишами с более естественной вариацией
                pause_factor = 1.0
                if key == Keys.SPACE:
                    pause_factor = 1.5  # Более длинная пауза после прыжка
                elif key == Keys.SHIFT:
                    pause_factor = 0.8  # Короче пауза после спринта
                
                wait_time = random.uniform(0.05, 0.3) * pause_factor
                wait(wait_time)
            
            # Периодически выполняем комбо-действия
            if random.random() < 0.4:  # 40% шанс
                self.perform_action_combo()
            
            # Добавляем более естественную паузу между циклами
            if self.running:
                cycle_pause = random.uniform(0.15, 0.6)
                # Иногда делаем более длинную паузу, имитируя остановку игрока
                if random.random() < 0.15:  # 15% шанс
                    cycle_pause *= 2.5
                wait(cycle_pause)

    def run(self):
        """Starts the anti afk thread"""
        self.running = True
        try:
            if self.mode == Mode.LIGHT:
                self.light_mode()
            elif self.mode == Mode.HEAVY:
                self.heavy_mode()
        except Exception as e:
            print(f"Ошибка в потоке KeySender: {e}")
            self.running = False

    def stop(self):
        """Stops the anti afk thread"""
        self.running = False
