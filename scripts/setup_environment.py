#!/usr/bin/env python3
"""
One-click environment setup for ML training

Automatically installs dependencies and configures the environment
for training ML models with GPU acceleration.

Usage:
    python setup_environment.py
    python setup_environment.py --gpu-check
    python setup_environment.py --install-cuda
"""

import argparse
import subprocess
import sys
import os
import platform
from pathlib import Path

def check_python_version():
    """Check if Python version is compatible"""
    version = sys.version_info
    if version < (3, 8):
        print(f"❌ Python {version.major}.{version.minor} is not supported. Please use Python 3.8+")
        return False
    
    print(f"✅ Python {version.major}.{version.minor}.{version.micro} is compatible")
    return True

def check_pip():
    """Check if pip is available"""
    try:
        subprocess.run([sys.executable, "-m", "pip", "--version"], 
                      check=True, capture_output=True)
        print("✅ pip is available")
        return True
    except subprocess.CalledProcessError:
        print("❌ pip is not available")
        return False

def install_requirements():
    """Install Python dependencies"""
    print("📦 Installing Python dependencies...")
    
    requirements_file = Path(__file__).parent / "requirements.txt"
    
    if not requirements_file.exists():
        print(f"❌ requirements.txt not found at {requirements_file}")
        return False
    
    try:
        subprocess.run([
            sys.executable, "-m", "pip", "install", "-r", str(requirements_file)
        ], check=True)
        print("✅ Dependencies installed successfully")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Failed to install dependencies: {e}")
        return False

def check_gpu_support():
    """Check NVIDIA GPU and CUDA support"""
    print("🔍 Checking GPU support...")
    
    # Check if nvidia-smi is available
    try:
        result = subprocess.run(["nvidia-smi"], capture_output=True, text=True, check=True)
        print("✅ NVIDIA GPU detected:")
        
        # Parse GPU info
        lines = result.stdout.split('\n')
        for line in lines:
            if "RTX" in line or "GTX" in line or "Tesla" in line or "Quadro" in line:
                gpu_info = line.strip()
                if gpu_info:
                    print(f"   {gpu_info}")
        
        return True
        
    except (subprocess.CalledProcessError, FileNotFoundError):
        print("❌ No NVIDIA GPU detected or nvidia-smi not available")
        return False

def check_cuda_torch():
    """Check if PyTorch with CUDA is installed correctly"""
    print("🔍 Checking PyTorch CUDA support...")
    
    try:
        import torch
        if torch.cuda.is_available():
            device_count = torch.cuda.device_count()
            current_device = torch.cuda.current_device()
            device_name = torch.cuda.get_device_name(current_device)
            cuda_version = torch.version.cuda
            
            print(f"✅ PyTorch CUDA support is available")
            print(f"   CUDA version: {cuda_version}")
            print(f"   GPU devices: {device_count}")
            print(f"   Current device: {device_name}")
            
            # Test GPU computation
            x = torch.randn(1000, 1000).cuda()
            y = torch.randn(1000, 1000).cuda()
            z = torch.mm(x, y)
            print(f"   GPU computation test: ✅ Success")
            
            return True
        else:
            print("❌ PyTorch CUDA support not available")
            return False
            
    except ImportError:
        print("❌ PyTorch not installed")
        return False
    except Exception as e:
        print(f"❌ Error testing CUDA: {e}")
        return False

def install_cuda_pytorch():
    """Install CUDA-enabled PyTorch"""
    print("🚀 Installing CUDA-enabled PyTorch...")
    
    # Uninstall existing PyTorch
    try:
        subprocess.run([
            sys.executable, "-m", "pip", "uninstall", "-y", 
            "torch", "torchvision", "torchaudio"
        ], check=True)
    except subprocess.CalledProcessError:
        pass  # Ignore if not installed
    
    # Install CUDA version
    try:
        subprocess.run([
            sys.executable, "-m", "pip", "install",
            "torch", "torchvision", "torchaudio",
            "--index-url", "https://download.pytorch.org/whl/cu118"
        ], check=True)
        print("✅ CUDA-enabled PyTorch installed")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Failed to install CUDA PyTorch: {e}")
        return False

def test_musicbrainz_connection(mb_url: str = "http://192.168.2.13:5001/"):
    """Test connection to MusicBrainz instance"""
    print(f"🔍 Testing MusicBrainz connection to {mb_url}...")
    
    try:
        import requests
        response = requests.get(f"{mb_url}ws/2/artist/5b11f4ce-a62d-471e-81fc-a69a8278c7da", 
                              timeout=10)
        if response.status_code == 200:
            print("✅ MusicBrainz connection successful")
            return True
        else:
            print(f"❌ MusicBrainz returned status {response.status_code}")
            return False
    except Exception as e:
        print(f"❌ MusicBrainz connection failed: {e}")
        print(f"   Make sure your MusicBrainz instance is running at {mb_url}")
        return False

def create_test_script():
    """Create a simple test script"""
    test_script = """#!/usr/bin/env python3
# Quick test script for ML training environment

import sys
print(f"Python: {sys.version}")

try:
    import torch
    print(f"PyTorch: {torch.__version__}")
    print(f"CUDA available: {torch.cuda.is_available()}")
    if torch.cuda.is_available():
        print(f"CUDA device: {torch.cuda.get_device_name()}")
        
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
"""
    
    with open("test_environment.py", "w") as f:
        f.write(test_script)
    
    print("✅ Created test_environment.py")

def main():
    parser = argparse.ArgumentParser(description="Setup ML training environment")
    parser.add_argument('--gpu-check', action='store_true', 
                       help='Only check GPU support')
    parser.add_argument('--install-cuda', action='store_true',
                       help='Install CUDA-enabled PyTorch')
    parser.add_argument('--mb-url', default='http://192.168.2.13:5001/',
                       help='MusicBrainz instance URL to test')
    
    args = parser.parse_args()
    
    print("🚀 Qobuzarr ML Training Environment Setup")
    print("=" * 50)
    
    if args.gpu_check:
        check_gpu_support()
        check_cuda_torch()
        return
    
    if args.install_cuda:
        install_cuda_pytorch()
        check_cuda_torch()
        return
    
    # Full setup
    success = True
    
    # Check prerequisites
    success &= check_python_version()
    success &= check_pip()
    
    if not success:
        print("❌ Prerequisites not met. Please fix the issues above.")
        sys.exit(1)
    
    # Install dependencies
    success &= install_requirements()
    
    # Check GPU support
    gpu_available = check_gpu_support()
    if gpu_available:
        cuda_success = check_cuda_torch()
        if not cuda_success:
            print("⚠️  Installing CUDA-enabled PyTorch...")
            install_cuda_pytorch()
            check_cuda_torch()
    
    # Test MusicBrainz connection
    test_musicbrainz_connection(args.mb_url)
    
    # Create test script
    create_test_script()
    
    print("\n" + "=" * 50)
    if success:
        print("✅ Setup completed successfully!")
        print("\n📋 Next steps:")
        print("1. Run: python test_environment.py")
        print("2. Extract data: python extract_musicbrainz_data.py --mb-url http://192.168.2.13:5001/ --output albums.json")
        print("3. Train model: python train_ml_model.py --input albums.json --gpu --epochs 100")
        print("4. Export to C#: python export_model_to_csharp.py --model trained_model.pth --output MyModel.cs")
    else:
        print("❌ Setup encountered issues. Please check the errors above.")
        sys.exit(1)

if __name__ == '__main__':
    main()