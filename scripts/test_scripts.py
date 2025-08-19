#!/usr/bin/env python3
"""
Test Script for ML Training Pipeline

Tests all components with minimal data to verify functionality
before running full training.

Usage:
    python test_scripts.py
    python test_scripts.py --mock-musicbrainz
"""

import argparse
import json
import logging
import os
import sys
import tempfile
import time
from pathlib import Path
from typing import List, Dict, Any

# Mock data for testing without MusicBrainz
MOCK_ALBUMS = [
    {
        "lidarr_id": 1,
        "artist_name": "The Beatles",
        "artist_id": "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d",
        "album_title": "Abbey Road",
        "album_title_clean": "Abbey Road",
        "album_type": "Album",
        "release_date": "1969-09-26",
        "release_year": "1969",
        "track_count": 17,
        "monitored": True,
        "search_query": "The Beatles Abbey Road",
        "disambiguation": "",
        "foreign_album_id": "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d",
        "genres": ["rock", "pop"],
        "overview": "",
        "album_id": 1,
        "artist_metadata_id": 123456
    },
    {
        "lidarr_id": 2,
        "artist_name": "Taylor Swift",
        "artist_id": "20244d07-534f-4eff-b4d4-930878889970",
        "album_title": "1989",
        "album_title_clean": "1989",
        "album_type": "Album",
        "release_date": "2014-10-27",
        "release_year": "2014",
        "track_count": 13,
        "monitored": True,
        "search_query": "Taylor Swift 1989",
        "disambiguation": "",
        "foreign_album_id": "20244d07-534f-4eff-b4d4-930878889970",
        "genres": ["pop"],
        "overview": "",
        "album_id": 2,
        "artist_metadata_id": 789012
    },
    {
        "lidarr_id": 3,
        "artist_name": "Various Artists",
        "artist_id": "89ad4ac3-39f7-470e-963a-56509c546377",
        "album_title": "Now That's What I Call Music! 85 (Deluxe Edition)",
        "album_title_clean": "Now That's What I Call Music! 85",
        "album_type": "Compilation",
        "release_date": "2023-07-21",
        "release_year": "2023",
        "track_count": 42,
        "monitored": True,
        "search_query": "Various Artists Now That's What I Call Music! 85",
        "disambiguation": "",
        "foreign_album_id": "89ad4ac3-39f7-470e-963a-56509c546377",
        "genres": ["pop", "electronic"],
        "overview": "",
        "album_id": 3,
        "artist_metadata_id": 345678
    },
    {
        "lidarr_id": 4,
        "artist_name": "Miles Davis",
        "artist_id": "561d854a-6a28-4aa7-8c99-323e6ce46c2a",
        "album_title": "Kind of Blue (Remastered)",
        "album_title_clean": "Kind of Blue",
        "album_type": "Album",
        "release_date": "1959-08-17",
        "release_year": "1959",
        "track_count": 5,
        "monitored": True,
        "search_query": "Miles Davis Kind of Blue",
        "disambiguation": "",
        "foreign_album_id": "561d854a-6a28-4aa7-8c99-323e6ce46c2a",
        "genres": ["jazz"],
        "overview": "",
        "album_id": 4,
        "artist_metadata_id": 901234
    },
    {
        "lidarr_id": 5,
        "artist_name": "Björk",
        "artist_id": "87c5dedd-371d-4a53-9f7f-80522fb7f3cb",
        "album_title": "Homogenic",
        "album_title_clean": "Homogenic",
        "album_type": "Album",
        "release_date": "1997-09-23",
        "release_year": "1997",
        "track_count": 10,
        "monitored": True,
        "search_query": "Björk Homogenic",
        "disambiguation": "",
        "foreign_album_id": "87c5dedd-371d-4a53-9f7f-80522fb7f3cb",
        "genres": ["electronic", "experimental"],
        "overview": "",
        "album_id": 5,
        "artist_metadata_id": 567890
    }
]

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class ScriptTester:
    """Tests ML training scripts functionality"""
    
    def __init__(self, use_mock: bool = True):
        self.use_mock = use_mock
        self.temp_dir = Path(tempfile.mkdtemp(prefix="qobuzarr_test_"))
        self.results = {}
        
        logger.info(f"Test directory: {self.temp_dir}")
    
    def cleanup(self):
        """Clean up test files"""
        import shutil
        try:
            shutil.rmtree(self.temp_dir)
            logger.info("Cleaned up test directory")
        except Exception as e:
            logger.warning(f"Failed to cleanup: {e}")
    
    def test_data_extraction(self) -> bool:
        """Test data extraction functionality"""
        logger.info("🧪 Testing data extraction...")
        
        if self.use_mock:
            # Create mock dataset
            dataset = {
                "version": "1.0.0",
                "created_at": time.strftime('%Y-%m-%d %H:%M:%S'),
                "source": "Mock data for testing",
                "total_albums": len(MOCK_ALBUMS),
                "albums": MOCK_ALBUMS
            }
            
            test_data_file = self.temp_dir / "test_albums.json"
            with open(test_data_file, 'w', encoding='utf-8') as f:
                json.dump(dataset, f, indent=2)
            
            logger.info(f"✅ Mock data created: {len(MOCK_ALBUMS)} albums")
            self.results['data_extraction'] = {
                'success': True,
                'albums_count': len(MOCK_ALBUMS),
                'file_path': str(test_data_file)
            }
            return True
        else:
            # Test real MusicBrainz extraction with minimal data
            try:
                import subprocess
                cmd = [
                    sys.executable, "extract_musicbrainz_data.py",
                    "--mb-url", "http://192.168.2.13:5001/",
                    "--output", str(self.temp_dir / "real_test_data.json"),
                    "--max-albums", "10",
                    "--incremental-size", "5"
                ]
                
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
                
                if result.returncode == 0:
                    logger.info("✅ Real data extraction successful")
                    self.results['data_extraction'] = {
                        'success': True,
                        'output': result.stdout
                    }
                    return True
                else:
                    logger.error(f"❌ Data extraction failed: {result.stderr}")
                    self.results['data_extraction'] = {
                        'success': False,
                        'error': result.stderr
                    }
                    return False
                    
            except Exception as e:
                logger.error(f"❌ Data extraction test failed: {e}")
                self.results['data_extraction'] = {
                    'success': False,
                    'error': str(e)
                }
                return False
    
    def test_feature_extraction(self) -> bool:
        """Test feature extraction functionality"""
        logger.info("🧪 Testing feature extraction...")
        
        try:
            # Import and test feature extractor
            sys.path.append(str(Path(__file__).parent))
            from train_ml_model import FeatureExtractor
            
            extractor = FeatureExtractor()
            features = extractor.extract_features(MOCK_ALBUMS)
            
            expected_shape = (len(MOCK_ALBUMS), 25)
            actual_shape = features.shape
            
            if actual_shape == expected_shape:
                logger.info(f"✅ Feature extraction successful: {actual_shape}")
                self.results['feature_extraction'] = {
                    'success': True,
                    'shape': actual_shape,
                    'sample_features': features[0].tolist()[:5]  # First 5 features of first album
                }
                return True
            else:
                logger.error(f"❌ Feature shape mismatch: expected {expected_shape}, got {actual_shape}")
                self.results['feature_extraction'] = {
                    'success': False,
                    'error': f"Shape mismatch: expected {expected_shape}, got {actual_shape}"
                }
                return False
                
        except Exception as e:
            logger.error(f"❌ Feature extraction test failed: {e}")
            self.results['feature_extraction'] = {
                'success': False,
                'error': str(e)
            }
            return False
    
    def test_model_training(self) -> bool:
        """Test model training with minimal data"""
        logger.info("🧪 Testing model training...")
        
        try:
            # Create test dataset file
            test_data_file = self.temp_dir / "training_test_data.json"
            
            # Expand mock data for training (need more samples)
            expanded_albums = MOCK_ALBUMS * 20  # 100 samples
            for i, album in enumerate(expanded_albums):
                album = album.copy()
                album['lidarr_id'] = i + 1
                album['album_id'] = i + 1
                # Add some variation
                if i % 3 == 0:
                    album['album_title'] += f" (Variation {i})"
                if i % 4 == 0:
                    album['album_type'] = "EP"
            
            dataset = {
                "version": "1.0.0",
                "created_at": time.strftime('%Y-%m-%d %H:%M:%S'),
                "source": "Expanded mock data for training test",
                "total_albums": len(expanded_albums),
                "albums": expanded_albums
            }
            
            with open(test_data_file, 'w', encoding='utf-8') as f:
                json.dump(dataset, f, indent=2)
            
            # Test training with minimal epochs
            import subprocess
            model_file = self.temp_dir / "test_model.pth"
            
            cmd = [
                sys.executable, "train_ml_model.py",
                "--input", str(test_data_file),
                "--output", str(model_file),
                "--epochs", "3",
                "--batch-size", "16",
                "--cpu"  # Force CPU for testing
            ]
            
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)  # 5 min timeout
            
            if result.returncode == 0 and model_file.exists():
                logger.info("✅ Model training successful")
                self.results['model_training'] = {
                    'success': True,
                    'model_file': str(model_file),
                    'output': result.stdout[-500:]  # Last 500 chars
                }
                return True
            else:
                logger.error(f"❌ Model training failed: {result.stderr}")
                self.results['model_training'] = {
                    'success': False,
                    'error': result.stderr,
                    'stdout': result.stdout
                }
                return False
                
        except Exception as e:
            logger.error(f"❌ Model training test failed: {e}")
            self.results['model_training'] = {
                'success': False,
                'error': str(e)
            }
            return False
    
    def test_csharp_export(self) -> bool:
        """Test C# code generation"""
        logger.info("🧪 Testing C# export...")
        
        # First need a trained model
        if not self.results.get('model_training', {}).get('success'):
            logger.warning("⚠️ Skipping C# export test - no trained model available")
            return True
        
        try:
            model_file = self.results['model_training']['model_file']
            csharp_file = self.temp_dir / "TestMLOptimizer.cs"
            
            import subprocess
            cmd = [
                sys.executable, "export_model_to_csharp.py",
                "--model", model_file,
                "--output", str(csharp_file),
                "--class", "TestMLOptimizer"
            ]
            
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
            
            if result.returncode == 0 and csharp_file.exists():
                # Verify C# file contents
                with open(csharp_file, 'r') as f:
                    csharp_content = f.read()
                
                required_elements = [
                    "class TestMLOptimizer",
                    "IPatternLearningEngine",
                    "PredictComplexity",
                    "QueryComplexity"
                ]
                
                missing_elements = [elem for elem in required_elements if elem not in csharp_content]
                
                if not missing_elements:
                    logger.info("✅ C# export successful")
                    self.results['csharp_export'] = {
                        'success': True,
                        'file_path': str(csharp_file),
                        'file_size': len(csharp_content)
                    }
                    return True
                else:
                    logger.error(f"❌ C# export incomplete - missing: {missing_elements}")
                    self.results['csharp_export'] = {
                        'success': False,
                        'error': f"Missing elements: {missing_elements}"
                    }
                    return False
            else:
                logger.error(f"❌ C# export failed: {result.stderr}")
                self.results['csharp_export'] = {
                    'success': False,
                    'error': result.stderr
                }
                return False
                
        except Exception as e:
            logger.error(f"❌ C# export test failed: {e}")
            self.results['csharp_export'] = {
                'success': False,
                'error': str(e)
            }
            return False
    
    def test_environment_setup(self) -> bool:
        """Test environment and dependencies"""
        logger.info("🧪 Testing environment setup...")
        
        results = {
            'python_version': sys.version_info[:3],
            'dependencies': {},
            'gpu_available': False
        }
        
        # Test required imports
        required_packages = [
            'numpy', 'pandas', 'torch', 'sklearn', 
            'matplotlib', 'seaborn', 'tqdm'
        ]
        
        missing_packages = []
        for package in required_packages:
            try:
                __import__(package)
                results['dependencies'][package] = '✅ Available'
            except ImportError:
                missing_packages.append(package)
                results['dependencies'][package] = '❌ Missing'
        
        # Test GPU availability
        try:
            import torch
            if torch.cuda.is_available():
                results['gpu_available'] = True
                results['gpu_name'] = torch.cuda.get_device_name()
                results['cuda_version'] = torch.version.cuda
        except:
            pass
        
        success = len(missing_packages) == 0
        
        if success:
            logger.info("✅ Environment setup OK")
        else:
            logger.error(f"❌ Missing packages: {missing_packages}")
        
        self.results['environment'] = results
        return success
    
    def run_all_tests(self) -> Dict[str, Any]:
        """Run all tests and return results"""
        logger.info("🚀 Starting ML training pipeline tests...")
        
        tests = [
            ('Environment Setup', self.test_environment_setup),
            ('Data Extraction', self.test_data_extraction),
            ('Feature Extraction', self.test_feature_extraction),
            ('Model Training', self.test_model_training),
            ('C# Export', self.test_csharp_export)
        ]
        
        test_results = {}
        passed = 0
        total = len(tests)
        
        for test_name, test_func in tests:
            logger.info(f"\n--- {test_name} ---")
            try:
                success = test_func()
                test_results[test_name] = success
                if success:
                    passed += 1
                    logger.info(f"✅ {test_name}: PASSED")
                else:
                    logger.error(f"❌ {test_name}: FAILED")
            except Exception as e:
                logger.error(f"💥 {test_name}: CRASHED - {e}")
                test_results[test_name] = False
        
        # Summary
        logger.info("\n" + "="*50)
        logger.info("TEST SUMMARY")
        logger.info("="*50)
        logger.info(f"Passed: {passed}/{total}")
        
        for test_name, success in test_results.items():
            status = "✅ PASS" if success else "❌ FAIL"
            logger.info(f"{test_name}: {status}")
        
        overall_success = passed == total
        if overall_success:
            logger.info("\n🎉 All tests passed! The ML training pipeline is ready to use.")
        else:
            logger.info(f"\n⚠️  {total - passed} test(s) failed. Check the errors above.")
        
        return {
            'overall_success': overall_success,
            'passed': passed,
            'total': total,
            'test_results': test_results,
            'detailed_results': self.results
        }

def main():
    parser = argparse.ArgumentParser(description="Test ML training scripts")
    parser.add_argument('--mock-musicbrainz', action='store_true',
                       help='Use mock data instead of real MusicBrainz connection')
    parser.add_argument('--output', default='test_results.json',
                       help='Output file for test results')
    
    args = parser.parse_args()
    
    tester = ScriptTester(use_mock=args.mock_musicbrainz)
    
    try:
        results = tester.run_all_tests()
        
        # Save results
        with open(args.output, 'w') as f:
            json.dump(results, f, indent=2, default=str)
        
        logger.info(f"\n📊 Detailed results saved to: {args.output}")
        
        # Exit with appropriate code
        sys.exit(0 if results['overall_success'] else 1)
        
    finally:
        tester.cleanup()

if __name__ == '__main__':
    main()