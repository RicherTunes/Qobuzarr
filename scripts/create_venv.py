#!/usr/bin/env python3
"""
Virtual Environment Setup for Qobuzarr ML Training

Creates and configures a dedicated Python virtual environment
with all dependencies for ML training on RTX 3090.

Usage:
    python create_venv.py
    python create_venv.py --name qobuzarr-ml --python 3.9
"""

import argparse
import subprocess
import sys
import os
import platform
from pathlib import Path

def run_command(cmd, description="", check=True):
    """Run command with proper error handling"""
    print(f"🔄 {description}")
    print(f"Command: {' '.join(cmd) if isinstance(cmd, list) else cmd}")
    
    try:
        if isinstance(cmd, str):
            result = subprocess.run(cmd, shell=True, check=check, capture_output=True, text=True)
        else:
            result = subprocess.run(cmd, check=check, capture_output=True, text=True)
        
        if result.stdout:
            print(f"✅ {result.stdout.strip()}")
        
        return result.returncode == 0
    except subprocess.CalledProcessError as e:
        print(f"❌ Error: {e}")
        if e.stderr:
            print(f"Error details: {e.stderr}")
        return False

def check_python_version():
    """Check if Python version is compatible"""
    version = sys.version_info
    if version < (3, 8):
        print(f"❌ Python {version.major}.{version.minor} is too old. Need Python 3.8+")
        return False
    
    print(f"✅ Python {version.major}.{version.minor}.{version.micro} is compatible")
    return True

def create_virtual_environment(venv_name, python_executable=None):
    """Create virtual environment"""
    venv_path = Path.cwd() / venv_name
    
    if venv_path.exists():
        print(f"⚠️  Virtual environment '{venv_name}' already exists")
        response = input("Delete and recreate? (y/N): ").lower()
        if response == 'y':
            import shutil
            shutil.rmtree(venv_path)
            print(f"🗑️  Deleted existing environment")
        else:
            print("Using existing environment")
            return venv_path
    
    # Create virtual environment
    cmd = [python_executable or sys.executable, "-m", "venv", str(venv_path)]
    success = run_command(cmd, f"Creating virtual environment '{venv_name}'")
    
    if not success:
        print("❌ Failed to create virtual environment")
        return None
    
    print(f"✅ Virtual environment created at: {venv_path}")
    return venv_path

def get_activation_command(venv_path):
    """Get the activation command for the virtual environment"""
    system = platform.system().lower()
    
    if system == "windows":
        activate_script = venv_path / "Scripts" / "activate.bat"
        return str(activate_script)
    else:
        activate_script = venv_path / "bin" / "activate"
        return f"source {activate_script}"

def install_dependencies(venv_path):
    """Install Python dependencies in virtual environment"""
    system = platform.system().lower()
    
    if system == "windows":
        python_exe = venv_path / "Scripts" / "python.exe"
        pip_exe = venv_path / "Scripts" / "pip.exe"
    else:
        python_exe = venv_path / "bin" / "python"
        pip_exe = venv_path / "bin" / "pip"
    
    # Upgrade pip first
    success = run_command([str(pip_exe), "install", "--upgrade", "pip"], 
                         "Upgrading pip")
    if not success:
        return False
    
    # Install requirements from file
    requirements_file = Path.cwd() / "requirements.txt"
    if requirements_file.exists():
        success = run_command([str(pip_exe), "install", "-r", str(requirements_file)], 
                             "Installing dependencies from requirements.txt")
        if not success:
            return False
    else:
        print("⚠️  requirements.txt not found, installing basic dependencies")
        
        # Install essential packages manually
        essential_packages = [
            "torch>=1.11.0",
            "torchvision>=0.12.0", 
            "torchaudio>=0.11.0",
            "numpy>=1.21.0",
            "pandas>=1.3.0",
            "scikit-learn>=1.0.0",
            "matplotlib>=3.5.0",
            "seaborn>=0.11.0",
            "tqdm>=4.64.0",
            "requests>=2.28.0",
            "psycopg2-binary>=2.9.0"
        ]
        
        for package in essential_packages:
            success = run_command([str(pip_exe), "install", package], 
                                 f"Installing {package}")
            if not success:
                print(f"⚠️  Failed to install {package}, continuing...")
    
    return True

def install_cuda_pytorch(venv_path):
    """Install CUDA-enabled PyTorch"""
    system = platform.system().lower()
    
    if system == "windows":
        pip_exe = venv_path / "Scripts" / "pip.exe"
    else:
        pip_exe = venv_path / "bin" / "pip"
    
    print("🚀 Installing CUDA-enabled PyTorch for RTX 3090...")
    
    # Uninstall existing PyTorch
    run_command([str(pip_exe), "uninstall", "-y", "torch", "torchvision", "torchaudio"], 
                "Removing existing PyTorch", check=False)
    
    # Install CUDA version
    cuda_packages = [
        "torch",
        "torchvision", 
        "torchaudio",
        "--index-url", "https://download.pytorch.org/whl/cu118"
    ]
    
    success = run_command([str(pip_exe), "install"] + cuda_packages,
                         "Installing CUDA-enabled PyTorch")
    
    return success

def test_installation(venv_path):
    """Test the installation"""
    system = platform.system().lower()
    
    if system == "windows":
        python_exe = venv_path / "Scripts" / "python.exe"
    else:
        python_exe = venv_path / "bin" / "python"
    
    test_script = '''
import sys
print(f"Python: {sys.version}")

try:
    import torch
    print(f"PyTorch: {torch.__version__}")
    print(f"CUDA available: {torch.cuda.is_available()}")
    if torch.cuda.is_available():
        print(f"CUDA device: {torch.cuda.get_device_name()}")
        print(f"CUDA version: {torch.version.cuda}")
        
        # Test GPU computation
        x = torch.randn(100, 100).cuda()
        y = torch.randn(100, 100).cuda()
        z = torch.mm(x, y)
        print("✅ GPU computation test: SUCCESS")
    else:
        print("⚠️  CUDA not available")
        
    import numpy as np
    print(f"NumPy: {np.__version__}")
    
    import pandas as pd
    print(f"Pandas: {pd.__version__}")
    
    import sklearn
    print(f"Scikit-learn: {sklearn.__version__}")
    
    print("\\n✅ All dependencies are working correctly!")
    
except ImportError as e:
    print(f"❌ Import error: {e}")
    sys.exit(1)
'''
    
    success = run_command([str(python_exe), "-c", test_script], 
                         "Testing installation")
    
    return success

def create_activation_scripts(venv_path, venv_name):
    """Create convenient activation scripts"""
    
    # Create Windows batch file
    windows_script = f'''@echo off
echo 🚀 Activating Qobuzarr ML Training Environment...
call "{venv_path}\\Scripts\\activate.bat"
echo ✅ Environment activated. You can now run:
echo    python quick_start.py --mb-url http://192.168.2.13:5001/ --fast-mode
echo    python test_scripts.py --mock-musicbrainz
echo.
cmd /k
'''
    
    with open("activate_ml_env.bat", "w") as f:
        f.write(windows_script)
    
    # Create Unix shell script
    unix_script = f'''#!/bin/bash
echo "🚀 Activating Qobuzarr ML Training Environment..."
source "{venv_path}/bin/activate"
echo "✅ Environment activated. You can now run:"
echo "   python quick_start.py --mb-url http://192.168.2.13:5001/ --fast-mode"
echo "   python test_scripts.py --mock-musicbrainz"
echo ""
exec "$SHELL"
'''
    
    with open("activate_ml_env.sh", "w") as f:
        f.write(unix_script)
    
    # Make shell script executable
    try:
        os.chmod("activate_ml_env.sh", 0o755)
    except:
        pass
    
    print("✅ Created activation scripts:")
    print("   Windows: activate_ml_env.bat")
    print("   Unix: ./activate_ml_env.sh")

def main():
    parser = argparse.ArgumentParser(description="Create virtual environment for Qobuzarr ML training")
    parser.add_argument('--name', default='qobuzarr-ml', help='Virtual environment name')
    parser.add_argument('--python', help='Python executable to use')
    parser.add_argument('--cuda', action='store_true', help='Install CUDA-enabled PyTorch')
    parser.add_argument('--test-only', action='store_true', help='Only test existing environment')
    
    args = parser.parse_args()
    
    print("🚀 Qobuzarr ML Training Environment Setup")
    print("=" * 50)
    
    if not check_python_version():
        sys.exit(1)
    
    venv_path = Path.cwd() / args.name
    
    if args.test_only:
        if venv_path.exists():
            print(f"Testing existing environment: {venv_path}")
            success = test_installation(venv_path)
            sys.exit(0 if success else 1)
        else:
            print(f"❌ Environment {args.name} does not exist")
            sys.exit(1)
    
    # Create virtual environment
    venv_path = create_virtual_environment(args.name, args.python)
    if not venv_path:
        sys.exit(1)
    
    # Install dependencies
    print("\n📦 Installing dependencies...")
    success = install_dependencies(venv_path)
    if not success:
        print("❌ Failed to install basic dependencies")
        sys.exit(1)
    
    # Install CUDA PyTorch if requested or auto-detect GPU
    install_cuda = args.cuda
    if not install_cuda:
        # Try to auto-detect NVIDIA GPU
        try:
            result = subprocess.run(["nvidia-smi"], capture_output=True, text=True)
            if result.returncode == 0:
                print("🎮 NVIDIA GPU detected, installing CUDA PyTorch...")
                install_cuda = True
        except FileNotFoundError:
            print("ℹ️  No NVIDIA GPU detected, using CPU-only PyTorch")
    
    if install_cuda:
        success = install_cuda_pytorch(venv_path)
        if not success:
            print("⚠️  CUDA PyTorch installation failed, but continuing...")
    
    # Test installation
    print("\n🧪 Testing installation...")
    success = test_installation(venv_path)
    if not success:
        print("❌ Installation test failed")
        sys.exit(1)
    
    # Create activation scripts
    create_activation_scripts(venv_path, args.name)
    
    # Success message
    print("\n" + "=" * 50)
    print("✅ Virtual environment setup completed successfully!")
    print("=" * 50)
    
    activation_cmd = get_activation_command(venv_path)
    print(f"📁 Environment location: {venv_path}")
    print(f"🔧 Activation command: {activation_cmd}")
    
    print("\n📋 Next steps:")
    print("1. Activate environment:")
    
    if platform.system().lower() == "windows":
        print("   activate_ml_env.bat")
    else:
        print("   ./activate_ml_env.sh")
    
    print("2. Test the scripts:")
    print("   python test_scripts.py --mock-musicbrainz")
    print("3. Quick training test:")
    print("   python quick_start.py --mb-url http://192.168.2.13:5001/ --fast-mode")
    print("4. Full production training:")
    print("   python train_production_model.py --help")
    
    print("\n🎉 Ready for ML training on your RTX 3090!")

if __name__ == '__main__':
    main()