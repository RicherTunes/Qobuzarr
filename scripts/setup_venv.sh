#!/bin/bash
# Quick virtual environment setup for Linux/macOS
# Run this script to create and configure the ML training environment

echo ""
echo "========================================"
echo "   Qobuzarr ML Training Environment"
echo "========================================"
echo ""

# Check if Python is available
if ! command -v python3 &> /dev/null; then
    echo "❌ Python3 not found. Please install Python 3.8+ first."
    exit 1
fi

echo "✅ Python found"
python3 --version

# Create virtual environment
echo ""
echo "🔄 Creating virtual environment..."
python3 create_venv.py --cuda

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Setup failed. Check the error messages above."
    exit 1
fi

echo ""
echo "========================================"
echo "          Setup Complete! 🎉"
echo "========================================"
echo ""
echo "To activate the environment, run:"
echo "    ./activate_ml_env.sh"
echo ""
echo "Then test with:"
echo "    python test_scripts.py --mock-musicbrainz"
echo ""