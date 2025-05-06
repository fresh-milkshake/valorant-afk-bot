#!/bin/bash

echo "Building Valorant-AntiAFK for Linux..."

# Create and activate virtual environment
python3 -m venv venv
source venv/bin/activate

# Install dependencies
python -m pip install --upgrade pip
pip install -r requirements.txt
pip install pyinstaller

# Build the application
pyinstaller --onefile --name "Valorant-AntiAFK-Linux" src/main.py

# Deactivate virtual environment
deactivate

echo "Build completed! Check the dist folder for the executable." 