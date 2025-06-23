from enum import Enum

Hexadecimal = int  # Hexadecimal value
Handle = int  # Window handle

class LoggingLevel(Enum):
    """
    Enum for the logging level
    """
    
    DEBUG = "DEBUG"
    INFO = "INFO"
    WARNING = "WARNING"
    ERROR = "ERROR"