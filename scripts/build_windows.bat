@echo off
echo Building Valorant-AntiAFK for Windows...

:: Create and activate virtual environment
python -m venv venv
call venv\Scripts\activate.bat

:: Install dependencies
python -m pip install --upgrade pip
pip install -r requirements.txt
pip install pyinstaller

:: Build the application
pyinstaller --onefile --name "Valorant-AntiAFK-Windows" src/main.py

:: Deactivate virtual environment
deactivate

echo Build completed! Check the dist folder for the executable.
pause 