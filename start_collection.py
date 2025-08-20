#!/usr/bin/env python3
"""
Easy starter script for continuous album data collection

Usage Examples:
    # Start collecting 10,000 albums for 95% accuracy
    python start_collection.py

    # Collect 5,000 albums for 90% accuracy  
    python start_collection.py --target 5000 --accuracy 0.90

    # Test with small collection
    python start_collection.py --test

    # Resume from existing database
    python start_collection.py --resume

    # Quick 2,000 album collection
    python start_collection.py --quick
"""

import argparse
import sys
import time
import subprocess
from pathlib import Path

def run_collection(target_size: int, min_accuracy: float, resume: bool = False):
    """Run the continuous collection with nice output"""
    
    print("Qobuzarr ML Training Data Collector")
    print("=" * 50)
    print(f"Target: {target_size:,} albums")
    print(f"Accuracy goal: {min_accuracy:.1%}")
    print(f"Database: album_collection.db")
    if resume:
        print("Resuming from existing database")
    print("=" * 50)
    print()
    
    print("Starting collection... (Press Ctrl+C to stop)")
    print()
    
    # Build command
    cmd = [
        'python', 'continuous_data_collector.py',
        '--target-size', str(target_size),
        '--min-accuracy', str(min_accuracy)
    ]
    
    try:
        # Run the collector
        subprocess.run(cmd, check=True)
        
        print()
        print("Collection completed successfully!")
        print("Check 'final_training_dataset.json' for your dataset")
        print("Trained model files saved with timestamp")
        
    except KeyboardInterrupt:
        print()
        print("Collection stopped by user")
        print("Progress saved in database")
        
    except subprocess.CalledProcessError as e:
        print(f"Collection failed with error: {e}")
        return False
        
    return True

def main():
    """Main function with preset configurations"""
    parser = argparse.ArgumentParser(description="Start album data collection for ML training")
    
    # Preset options
    parser.add_argument('--test', action='store_true',
                       help='Quick test (100 albums, no training)')
    parser.add_argument('--quick', action='store_true', 
                       help='Quick collection (2,000 albums, 90% accuracy)')
    parser.add_argument('--medium', action='store_true',
                       help='Medium collection (5,000 albums, 93% accuracy)')
    parser.add_argument('--large', action='store_true',
                       help='Large collection (10,000 albums, 95% accuracy)')
    parser.add_argument('--massive', action='store_true',
                       help='Massive collection (25,000 albums, 97% accuracy)')
    
    # Custom options
    parser.add_argument('--target', type=int, 
                       help='Custom target number of albums')
    parser.add_argument('--accuracy', type=float,
                       help='Custom accuracy target (0.90 = 90%)')
    parser.add_argument('--resume', action='store_true',
                       help='Resume from existing database')
    
    args = parser.parse_args()
    
    # Handle test mode
    if args.test:
        print("Running test collection...")
        try:
            result = subprocess.run([
                'python', 'continuous_data_collector.py', '--test-only'
            ], check=True)
            print("Test completed successfully!")
        except subprocess.CalledProcessError:
            print("Test failed")
        return
    
    # Determine target and accuracy
    if args.quick:
        target_size, min_accuracy = 2000, 0.90
    elif args.medium:
        target_size, min_accuracy = 5000, 0.93
    elif args.large:
        target_size, min_accuracy = 10000, 0.95
    elif args.massive:
        target_size, min_accuracy = 25000, 0.97
    else:
        # Use custom or defaults
        target_size = args.target or 10000
        min_accuracy = args.accuracy or 0.95
    
    # Validate inputs
    if target_size < 100:
        print("Target size must be at least 100 albums")
        return
    
    if not (0.5 <= min_accuracy <= 1.0):
        print("Accuracy must be between 0.5 and 1.0")
        return
    
    # Check if database exists for resume
    db_exists = Path("album_collection.db").exists()
    if args.resume and not db_exists:
        print("No existing database found to resume from")
        return
    
    if not args.resume and db_exists:
        response = input("Existing database found. Resume? (y/N): ")
        if response.lower().startswith('y'):
            args.resume = True
    
    # Estimate time
    estimated_hours = max(0.5, target_size / 2000)  # Rough estimate
    print(f"Estimated time: {estimated_hours:.1f} hours")
    print(f"Database will be saved continuously")
    print()
    
    if not args.test:
        response = input("Start collection? (Y/n): ")
        if response.lower().startswith('n'):
            print("Collection cancelled")
            return
    
    # Run collection
    success = run_collection(target_size, min_accuracy, args.resume)
    
    if success:
        print()
        print("Ready to use your new ML model!")
        print("Next steps:")
        print("   1. Copy the .pth model file to scripts/")
        print("   2. Update your Qobuzarr configuration")
        print("   3. Restart Lidarr to use the new model")

if __name__ == "__main__":
    main()