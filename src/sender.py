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

    LIGHT = "Jumping"
    HEAVY = "WASD"


class MovementPattern(Enum):
    """Different movement patterns for WASD mode"""
    RANDOM = "random"
    CIRCLE = "circle"
    STRAFE = "strafe"
    FORWARD_BACK = "forward_back"
    CUSTOM = "custom"


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
        self._last_window_check = 0
        self._window_check_interval = 10.0  # check window every 10 seconds

        super().__init__()
        self.daemon = True  # make thread daemon so it terminates with main
        self.running = False

        self._jump_delay = self.DEFAULT_LIGHT_DELAY
        self._last_action_time = time.time()
        
        # Extended set of random actions
        self._random_actions = [
            (Keys.SHIFT, 0.2),    # Running
            (Keys.CTRL, 0.15),    # Crouch
            (Keys.SPACE, 0.1),    # Jump
        ]
        
        # Random key combinations to simulate player actions
        self._action_combos = [
            [(Keys.W, 0.1), (Keys.SPACE, 0.1)],                   # Run and jump
            [(Keys.CTRL, 0.15), (Keys.W, 0.1)],                   # Crouch and walk
            [(Keys.SHIFT, 0.2), (Keys.W, 0.15)],   # Run forward
            [(Keys.A, 0.1), (Keys.D, 0.1)],                       # Strafe left-right
        ]

        if self.mode == Mode.HEAVY:
            self.path = None
            self._wasd_sequence = []
            self._key_press_time = self.DEFAULT_HEAVY_DELAY
            self._movement_path = self.DEFAULT_PATH
            self._combo_chance = 0.6  # Chance to perform key combination
            self._movement_pattern_count = 0  # Movement pattern counter
            self._max_movement_patterns = random.randint(3, 7)  # Max patterns before change
            
            # Enhanced WASD mode settings
            self._movement_intensity = 0.7  # How active the movement is (0.1-1.0)
            self._direction_change_frequency = 0.3  # How often to change direction (0.1-1.0)
            self._action_probability = 0.4  # Probability of additional actions (0.0-1.0)
            self._pattern_type = MovementPattern.RANDOM
            self._strafe_preference = 0.5  # Preference for strafing vs forward/back (0.0-1.0)
            self._movement_smoothness = 0.6  # How smooth transitions are (0.1-1.0)
            self._pause_frequency = 0.2  # How often to pause movement (0.0-1.0)
            self._pause_duration_range = (0.5, 2.0)  # Range for pause durations

    @property
    def _jump_delay_diff(self):
        """Returns the 1/4 of the jump delay"""
        return self._jump_delay / 4

    def update_settings(self, settings: dict):
        """Updates the settings of the KeySender thread"""
        if self.mode == Mode.LIGHT:
            self._jump_delay = float(settings.get("light_mode_delay", self._jump_delay))
            return

        # Basic WASD settings
        self._movement_path = settings.get("heavy_mode_path", self._movement_path)
        self._key_press_time = float(settings.get("heavy_mode_delay", self._key_press_time))
        
        # Advanced WASD settings
        self._movement_intensity = float(settings.get("movement_intensity", self._movement_intensity))
        self._direction_change_frequency = float(settings.get("direction_change_frequency", self._direction_change_frequency))
        self._action_probability = float(settings.get("action_probability", self._action_probability))
        self._strafe_preference = float(settings.get("strafe_preference", self._strafe_preference))
        self._movement_smoothness = float(settings.get("movement_smoothness", self._movement_smoothness))
        self._pause_frequency = float(settings.get("pause_frequency", self._pause_frequency))
        
        pattern_type = settings.get("pattern_type", "random")
        if isinstance(pattern_type, str):
            try:
                self._pattern_type = MovementPattern(pattern_type)
            except ValueError:
                self._pattern_type = MovementPattern.RANDOM
        
        # Reset counters to apply new settings
        self._movement_pattern_count = 0
        self._max_movement_patterns = random.randint(3, 7)

    @property
    def jump_delay(self):
        """Returns a random delay between the jump delay +- jump delay difference"""
        variation = self._jump_delay * 0.4  # 40% variation
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
            
        # Small random variation in press duration
        actual_delay = delay * random.uniform(0.85, 1.15)
        
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        wait(actual_delay)
        win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def send_key_combination(self, keys: list, durations: list):
        """Sends multiple keys simultaneously"""
        if not self.is_window_active():
            self.running = False
            return
            
        # Press all keys down
        for key in keys:
            win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYDOWN, key, 0)
        
        # Wait for the duration
        wait(max(durations) * random.uniform(0.9, 1.1))
        
        # Release all keys
        for key in keys:
            win32api.SendMessage(self.valorant_hwnd, win32con.WM_KEYUP, key, 0)

    def generate_movement_pattern(self):
        """Generate movement pattern based on selected type"""
        if self._pattern_type == MovementPattern.CIRCLE:
            return ["W", "WD", "D", "SD", "S", "SA", "A", "WA"]
        elif self._pattern_type == MovementPattern.STRAFE:
            return ["A", "D"] * random.randint(2, 4)
        elif self._pattern_type == MovementPattern.FORWARD_BACK:
            return ["W", "S"] * random.randint(2, 3)
        elif self._pattern_type == MovementPattern.CUSTOM:
            return list(self._movement_path.upper())
        else:  # RANDOM
            directions = []
            length = random.randint(3, 8)
            for _ in range(length):
                if random.random() < self._strafe_preference:
                    directions.append(random.choice(["A", "D"]))
                else:
                    directions.append(random.choice(["W", "S"]))
            return directions

    def perform_random_action(self):
        """Performs a random action with a small chance"""
        if random.random() < (0.2 * self._action_probability):
            key, duration = random.choice(self._random_actions)
            self.send_key(key, duration)
            
            # Sometimes add a second random action right after the first
            if random.random() < 0.3:
                wait(random.uniform(0.05, 0.2))
                second_key, second_duration = random.choice(self._random_actions)
                if second_key != key:  # Avoid repeating the same key
                    self.send_key(second_key, second_duration)

    def perform_action_combo(self):
        if random.random() < (self._combo_chance * self._action_probability):
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
                # With some probability perform combo instead of regular jump
                if combo_counter >= 3 and random.random() < 0.4:
                    # Perform combo actions
                    performed_combo = self.perform_action_combo()
                    if performed_combo:
                        combo_counter = 0
                else:
                    # Regular action - jump with variations
                    self.send_key(Keys.SPACE, self._press_duration * random.uniform(0.8, 1.2))
                    combo_counter += 1
                
                # Perform random action
                self.perform_random_action()
                
                # Sometimes do more complex sequences
                action_variance_counter += 1
                if action_variance_counter >= random.randint(3, 6) and random.random() < 0.35:
                    # Emulate equipment checking or other actions
                    action_variance_counter = 0
                    sequence = []
                    
                    # Random sequence of 2-4 keys
                    for _ in range(random.randint(2, 4)):
                        key, duration = random.choice(self._random_actions)
                        sequence.append((key, duration))
                    
                    # Execute sequence with different delays
                    for key, duration in sequence:
                        if not self.running:
                            break
                        self.send_key(key, duration)
                        # More natural pauses between presses
                        wait(random.uniform(0.05, 0.25))
                
                self._last_action_time = current_time
            
            # Dynamic delay to reduce CPU load
            wait(random.uniform(0.08, 0.15))

    def heavy_mode(self):
        """
        Enhanced WASD mode with more realistic movement patterns and customizable behavior
        """
        pattern_step = 0
        current_pattern = self.generate_movement_pattern()
        last_direction_change = time.time()
        
        while self.running:
            current_time = time.time()
            
            # Check if we should pause movement
            if random.random() < self._pause_frequency:
                pause_duration = random.uniform(*self._pause_duration_range)
                wait(pause_duration)
                continue
            
            # Update movement pattern based on frequency and intensity
            direction_change_interval = (2.0 - self._direction_change_frequency) * 3.0
            if (current_time - last_direction_change) >= direction_change_interval:
                current_pattern = self.generate_movement_pattern()
                pattern_step = 0
                last_direction_change = current_time
            
            # Get current movement direction
            if pattern_step >= len(current_pattern):
                pattern_step = 0
                
            direction = current_pattern[pattern_step]
            pattern_step += 1
            
            # Convert direction to keys
            movement_keys = []
            if "W" in direction:
                movement_keys.append(Keys.W)
            if "A" in direction:
                movement_keys.append(Keys.A)
            if "S" in direction:
                movement_keys.append(Keys.S)
            if "D" in direction:
                movement_keys.append(Keys.D)
            
            # Apply movement intensity
            base_duration = self._key_press_time
            movement_duration = base_duration * (0.5 + self._movement_intensity * 0.8)
            movement_duration *= random.uniform(0.8, 1.2)  # Add some randomness
            
            # Execute movement
            if movement_keys:
                if len(movement_keys) == 1:
                    self.send_key(movement_keys[0], movement_duration)
                else:
                    # Multiple keys (diagonal movement)
                    self.send_key_combination(movement_keys, [movement_duration] * len(movement_keys))
            
            # Add additional actions based on probability
            if random.random() < (0.4 * self._action_probability):
                if random.random() < 0.6:  # Jump
                    wait(random.uniform(0.05, 0.15))
                    self.send_key(Keys.SPACE, self._press_duration)
                elif random.random() < 0.3:  # Sprint
                    self.send_key(Keys.SHIFT, self._press_duration * 2)
                else:  # Crouch
                    self.send_key(Keys.CTRL, self._press_duration * 1.5)
            
            # Perform random additional actions
            self.perform_random_action()
            
            # Apply smoothness factor to pause between movements
            base_pause = 0.1
            smoothness_pause = base_pause * (2.0 - self._movement_smoothness)
            wait(random.uniform(smoothness_pause * 0.5, smoothness_pause * 1.5))
            
            # Occasionally perform combo actions
            if random.random() < (0.3 * self._action_probability):
                self.perform_action_combo()

    def run(self):
        """Starts the anti afk thread"""
        self.running = True
        try:
            if self.mode == Mode.LIGHT:
                self.light_mode()
            elif self.mode == Mode.HEAVY:
                self.heavy_mode()
        except Exception as e:
            print(f"Error in KeySender thread: {e}")
            self.running = False

    def stop(self):
        """Stops the anti afk thread"""
        self.running = False
