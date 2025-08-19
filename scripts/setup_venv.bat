@echo off
REM Quick virtual environment setup for Windows
REM Run this script to create and configure the ML training environment

echo.
echo ========================================
echo   Qobuzarr ML Training Environment
echo ========================================
echo.

REM Check if Python is available
python --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Python not found. Please install Python 3.8+ first.
    echo Download from: https://www.python.org/downloads/
    pause
    exit /b 1
)

echo ✅ Python found
python --version

REM Create virtual environment
echo.
echo 🔄 Creating virtual environment...
python create_venv.py --cuda

if errorlevel 1 (
    echo.
    echo ❌ Setup failed. Check the error messages above.
    pause
    exit /b 1
)

echo.
echo ========================================
echo          Setup Complete! 🎉
echo ========================================
echo.
echo To activate the environment, run:
echo    activate_ml_env.bat
echo.
echo Then test with:
echo    python test_scripts.py --mock-musicbrainz
echo.
pause