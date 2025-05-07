@echo off
echo Building Valorant Anti-AFK for Windows...

:: Check if Python is installed
python --version > nul 2>&1
if %errorlevel% neq 0 (
    echo Python is not installed or not in PATH. Please install Python 3.6+ and try again.
    exit /b 1
)

:: Check if pip is installed
pip --version > nul 2>&1
if %errorlevel% neq 0 (
    echo pip is not installed or not in PATH. Please install pip and try again.
    exit /b 1
)

:: Create virtual environment if it doesn't exist
if not exist venv (
    echo Creating virtual environment...
    python -m venv venv
)

:: Activate virtual environment
call venv\Scripts\activate

:: Install dependencies
echo Installing dependencies...
pip install PyQt6 pywin32 pyinstaller

:: Build executable with PyInstaller
echo Building executable...
pyinstaller --noconfirm --onefile --windowed --icon=assets/icon.ico --add-data="assets;assets/" --name="Valorant-AntiAFK" src/main.py

:: Deactivate virtual environment
call venv\Scripts\deactivate

echo Build completed successfully. Executable is located in the dist folder.
echo You can run the application by executing dist\Valorant-AntiAFK.exe
