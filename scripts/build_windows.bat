@echo off
setlocal

echo Building Valorant AFK Bot for Windows...

dotnet --info > nul 2>&1
if %errorlevel% neq 0 (
    echo .NET SDK 10.0 or newer is required and was not found in PATH.
    exit /b 1
)

set "OUTPUT_DIR=bin"
set "STAGING_DIR=%OUTPUT_DIR%\publish-tmp"
set "FINAL_EXE=%OUTPUT_DIR%\anti-afk.exe"
set "PUBLISH_EXE=%OUTPUT_DIR%\ValorantAfkBot.exe"
set "ROOT_DIR=%CD%"

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo Stopping running builds from %OUTPUT_DIR% if needed...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop_running_builds.ps1" -OutputDir "%ROOT_DIR%\%OUTPUT_DIR%"

if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
mkdir "%STAGING_DIR%" > nul 2>&1

del /f /q "%FINAL_EXE%" > nul 2>&1
del /f /q "%PUBLISH_EXE%" > nul 2>&1

if exist "%FINAL_EXE%" (
    if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
    echo Failed to remove the previous %FINAL_EXE%. Close the running app and try again.
    exit /b 1
)

if exist "%PUBLISH_EXE%" (
    if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
    echo Failed to remove the previous %PUBLISH_EXE%. Close the running app and try again.
    exit /b 1
)

dotnet publish src\ValorantAfk.App\ValorantAfk.App.csproj ^
  -c Release ^
  -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:SelfContained=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugSymbols=false ^
  -p:DebugType=None ^
  -o "%STAGING_DIR%"

if %errorlevel% neq 0 (
    if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
    echo Build failed.
    exit /b 1
)

if not exist "%STAGING_DIR%\ValorantAfkBot.exe" (
    if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
    echo Publish completed, but the expected executable was not produced.
    exit /b 1
)

move /y "%STAGING_DIR%\ValorantAfkBot.exe" "%FINAL_EXE%" > nul
if %errorlevel% neq 0 (
    if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
    echo Failed to move published executable into %FINAL_EXE%.
    exit /b 1
)

del /f /q "%PUBLISH_EXE%" > nul 2>&1
if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"

echo Build completed successfully.
echo Output: %FINAL_EXE%
