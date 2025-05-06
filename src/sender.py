import random
import threading
from enum import Enum

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


class Mode(Enum):
    """
    Enum for the KeySender working modes

    LIGHT - Just jumps with random delay between jumps and rare other key presses
    HEAVY - Moves along a random path and rarely does random jumps
    """
    LIGHT = 'Light'
    HEAVY = 'Heavy'


class KeySender(threading.Thread):
    """
    Main class, implementing Thread class's run and stop methods for sending
    key presses to the game separated from the GUI thread
    """
    AVAILABLE_MODES = list(Mode)
    MODES_NAMES = [mode.value for mode in AVAILABLE_MODES]
    DEFAULT_PRESS_DURATION = .1

    def __init__(self,
                 mode: Mode,
                 window_handle: Handle,
                 press_duration: float = DEFAULT_PRESS_DURATION):
        """
        Creates a new KeySender thread
        :param mode: The mode of the KeySender thread
        :param window_handle: The window handle of the game
        """

        if not isinstance(mode, Mode):
            mode = Mode(mode)

        if mode not in self.AVAILABLE_MODES:
            raise ValueError(f"Invalid mode: {mode}")

        if not win32gui.IsWindow(window_handle):
            raise ValueError(f"Invalid window handle: {window_handle}")

        self.mode = mode
        self.valorant_hwnd = window_handle

        super().__init__()
        self.running = False

        self._jump_delay = 5

        if self.mode == Mode.HEAVY:
            self.path = None
            self._wasd_sequence = []
            self._key_press_time = press_duration

    @property
    def _jump_delay_diff(self):
        """Returns the 1/4 of the jump delay"""
        return self._jump_delay / 4

    def update_settings(self, settings: dict):
        """Updates the settings of the KeySender thread"""
        print(settings)
        if self.mode == Mode.LIGHT:
            self._jump_delay = float(
                settings.get('light_mode_delay', self._jump_delay))
            return

        self._wasd_sequence = settings.get('heavy_mode_path',
                                           self._wasd_sequence)
        self._key_press_time = float(
            settings.get('heavy_mode_delay', self._key_press_time))

    @property
    def jump_delay(self):
        """Returns a random delay between the jump delay +- jump delay difference"""
        return random.uniform(self._jump_delay - self._jump_delay_diff,
                              self._jump_delay + self._jump_delay_diff)

    def send_key(self, key: Hexadecimal, delay: float):
        """Sends a key to the game"""
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(delay)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def light_mode(self):
        """
        Runs the light mode - just jumps with random delay between jumps and
        rare other key presses and mouse movements
        """

        while self.running:
            self.send_key(Keys.SPACE, self.DEFAULT_PRESS_DURATION)
            wait(self.jump_delay)

    def heavy_mode(self):
        """
        Runs the heavy mode - moves along a random path and rarely does random
        jumps and mouse movements
        """

        while self.running:
            self.path = [(Keys.W, self._key_press_time),
                         (Keys.A, self._key_press_time),
                         (Keys.S, self._key_press_time),
                         (Keys.D, self._key_press_time)]

            for key, time in self.path:
                self.send_key(key, time)

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