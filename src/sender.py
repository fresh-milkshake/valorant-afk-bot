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

    LIGHT = "Light"
    HEAVY = "Heavy"


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
            raise ValueError(f"Invalid mode: {mode}")

        if not win32gui.IsWindow(window_handle):
            raise ValueError(f"Invalid window handle: {window_handle}")

        self.mode = mode
        self.valorant_hwnd = window_handle
        self._press_duration = press_duration

        super().__init__()
        self.running = False

        self._jump_delay = self.DEFAULT_LIGHT_DELAY
        self._last_action_time = time.time()
        self._random_actions = [
            (Keys.SHIFT, 0.2),  # Sprint
            (Keys.CTRL, 0.2),  # Crouch
            (Keys.SPACE, 0.1),  # Jump
        ]

        if self.mode == Mode.HEAVY:
            self.path = None
            self._wasd_sequence = []
            self._key_press_time = self.DEFAULT_HEAVY_DELAY
            self._movement_path = self.DEFAULT_PATH

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

    @property
    def jump_delay(self):
        """Returns a random delay between the jump delay +- jump delay difference"""
        return random.uniform(
            self._jump_delay - self._jump_delay_diff,
            self._jump_delay + self._jump_delay_diff,
        )

    def send_key(self, key: Hexadecimal, delay: float):
        """Sends a key to the game"""
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(delay)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def perform_random_action(self):
        """Performs a random action with a small chance"""
        if random.random() < 0.1:  # 10% chance
            key, duration = random.choice(self._random_actions)
            self.send_key(key, duration)

    def light_mode(self):
        """
        Runs the light mode - just jumps with random delay between jumps and
        rare other key presses and mouse movements
        """
        while self.running:
            current_time = time.time()
            if current_time - self._last_action_time >= self.jump_delay:
                self.send_key(Keys.SPACE, self._press_duration)
                self.perform_random_action()
                self._last_action_time = current_time
            wait(0.1)  # Small delay to prevent high CPU usage

    def heavy_mode(self):
        """
        Runs the heavy mode - moves along a random path and rarely does random
        jumps and mouse movements
        """
        while self.running:
            # Convert movement path string to key sequence
            key_sequence = []
            for char in self._movement_path.upper():
                if char == "W":
                    key_sequence.append((Keys.W, self._key_press_time))
                elif char == "A":
                    key_sequence.append((Keys.A, self._key_press_time))
                elif char == "S":
                    key_sequence.append((Keys.S, self._key_press_time))
                elif char == "D":
                    key_sequence.append((Keys.D, self._key_press_time))

            # Add some randomness to the sequence
            random.shuffle(key_sequence)

            # Execute the sequence
            for key, t in key_sequence:
                if not self.running:
                    break
                self.send_key(key, t)
                self.perform_random_action()
                wait(random.uniform(0.1, 0.3))  # Random delay between movements

    def run(self):
        """Starts the anti afk thread"""
        self.running = True
        if self.mode == Mode.LIGHT:
            self.light_mode()
        elif self.mode == Mode.HEAVY:
            self.heavy_mode()

    def stop(self):
        """Stops the anti afk thread"""
        self.running = False
