#!/usr/bin/env python3
"""
Autonomous Library Analysis Test

Tests the library analysis pipeline autonomously using .env configuration.
Starts with a small sample to validate the approach before full-scale analysis.

Usage:
    python scripts/test_autonomous_analysis.py
"""

import asyncio
import subprocess
import sys
import json
from pathlib import Path
import logging
from load_env import get_config, check_credentials

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

async def test_autonomous_analysis():
    """Run autonomous test of the library analysis pipeline"""
    
    print("🧪 Autonomous Library Analysis Test")
    print("=" * 50)
    
    # Check credentials
    has_lidarr, has_qobuz, messages = check_credentials()
    
    for message in messages:
        print(message)
    
    if not has_lidarr:
        print("\n❌ Cannot proceed without Lidarr credentials")
        print("Please create scripts/.env from scripts/.env.example")
        return False
    
    config = get_config()
    
    print(f"\n🎯 Test Configuration:")
    print(f"   • Complexity threshold: {config['default_complexity_threshold']}")
    print(f"   • Sample size: 20 albums (test run)")
    print(f"   • Qobuz validation: {'Yes' if has_qobuz else 'No (will skip)'}")
    print("=" * 50)
    
    # Step 1: Extract small library sample
    print("\n📖 Step 1: Testing library extraction...")
    
    try:
        result = subprocess.run([
            sys.executable, 'scripts/extract_lidarr_library.py',
            '--limit', '20',
            '--complexity-threshold', '0.3',  # Lower threshold for test
            '--export-format', 'json',
            '--output', 'scripts/test_library_sample'
        ], capture_output=True, text=True, timeout=120)
        
        if result.returncode == 0:
            print("✅ Library extraction test successful")
            
            # Check if we got results
            if Path('scripts/test_library_sample.json').exists():
                with open('scripts/test_library_sample.json', 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    album_count = len(data.get('albums', []))
                    
                print(f"   📊 Extracted {album_count} complex albums")
                
                if album_count > 0:
                    # Show sample albums
                    print("   🎵 Sample complex albums:")
                    for album in data['albums'][:3]:
                        print(f"      • {album['artist']} - {album['album']} (complexity: {album['complexity_score']:.3f})")
                else:
                    print("   ⚠️ No complex albums found - try lowering complexity threshold")
            
        else:
            print(f"❌ Library extraction failed: {result.stderr}")
            return False
            
    except subprocess.TimeoutExpired:
        print("❌ Library extraction timed out")
        return False
    except Exception as e:
        print(f"❌ Error in library extraction: {e}")
        return False
    
    # Step 2: Test gap analysis
    print("\n🔍 Step 2: Testing Unicode gap analysis...")
    
    try:
        # Convert JSON to database format for gap analysis
        if Path('scripts/test_library_sample.json').exists():
            # Create a simple database for gap analysis
            import sqlite3
            
            conn = sqlite3.connect('scripts/test_sample.db')
            cursor = conn.cursor()
            
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS library_albums (
                    lidarr_id INTEGER PRIMARY KEY,
                    artist TEXT,
                    album TEXT,
                    complexity_score REAL,
                    complexity_factors TEXT
                )
            ''')
            
            with open('scripts/test_library_sample.json', 'r', encoding='utf-8') as f:
                data = json.load(f)
                
            for album in data.get('albums', []):
                cursor.execute('''
                    INSERT OR REPLACE INTO library_albums 
                    (lidarr_id, artist, album, complexity_score, complexity_factors)
                    VALUES (?, ?, ?, ?, ?)
                ''', (
                    album['lidarr_id'], album['artist'], album['album'],
                    album['complexity_score'], json.dumps(album['complexity_factors'])
                ))
            
            conn.commit()
            conn.close()
            
            # Run gap analysis
            result = subprocess.run([
                sys.executable, 'scripts/analyze_unicode_gaps.py',
                '--database', 'scripts/test_sample.db',
                '--min-complexity', '0.4',
                '--output', 'scripts/test_gaps.json'
            ], capture_output=True, text=True, timeout=60)
            
            if result.returncode == 0:
                print("✅ Gap analysis test successful")
                
                if Path('scripts/test_gaps.json').exists():
                    with open('scripts/test_gaps.json', 'r', encoding='utf-8') as f:
                        gap_data = json.load(f)
                        
                    gap_count = len(gap_data.get('gaps', []))
                    print(f"   🔍 Found {gap_count} potential gaps")
                    
                    if gap_count > 0:
                        print("   📝 Sample predicted gaps:")
                        for gap in gap_data['gaps'][:3]:
                            print(f"      • {gap['artist']} - {gap['album']}")
                            print(f"        Reason: {gap['predicted_failure_reason']}")
                else:
                    print("   ⚠️ No gap analysis results generated")
            else:
                print(f"❌ Gap analysis failed: {result.stderr}")
                return False
                
    except Exception as e:
        print(f"❌ Error in gap analysis: {e}")
        return False
    
    # Step 3: Test Qobuz validation (if credentials available)
    if has_qobuz:
        print("\n✅ Step 3: Testing Qobuz validation...")
        
        try:
            result = subprocess.run([
                sys.executable, 'scripts/validate_qobuz_gaps.py',
                '--gaps-file', 'scripts/test_gaps.json',
                '--max-validations', '5',  # Very small test
                '--output', 'scripts/test_validation.json'
            ], capture_output=True, text=True, timeout=300)  # 5 minutes max
            
            if result.returncode == 0:
                print("✅ Qobuz validation test successful")
                
                if Path('scripts/test_validation.json').exists():
                    with open('scripts/test_validation.json', 'r', encoding='utf-8') as f:
                        val_data = json.load(f)
                    
                    val_summary = val_data.get('analysis', {}).get('validation_summary', {})
                    print(f"   📊 Validated: {val_summary.get('total_validated', 0)} gaps")
                    print(f"   ✅ Confirmed gaps: {val_summary.get('confirmed_gaps', 0)}")
                    print(f"   ⚠️ False positives: {val_summary.get('false_positives', 0)}")
            else:
                print(f"❌ Qobuz validation failed: {result.stderr}")
                return False
                
        except subprocess.TimeoutExpired:
            print("❌ Qobuz validation timed out")
            return False
        except Exception as e:
            print(f"❌ Error in Qobuz validation: {e}")
            return False
    else:
        print("\n⚠️ Step 3: Skipping Qobuz validation (no credentials)")
    
    # Step 4: Test case generation
    print("\n🧪 Step 4: Testing test case generation...")
    
    try:
        result = subprocess.run([
            sys.executable, 'scripts/generate_complex_test_cases.py',
            '--validation-results', 'scripts/test_validation.json' if has_qobuz else '',
            '--gaps-analysis', 'scripts/test_gaps.json',
            '--output', 'scripts/test_generated_cases.cs',
            '--max-test-cases', '10'
        ], capture_output=True, text=True, timeout=60)
        
        if result.returncode == 0:
            print("✅ Test case generation successful")
            
            if Path('scripts/test_generated_cases.cs').exists():
                size_kb = Path('scripts/test_generated_cases.cs').stat().st_size // 1024
                print(f"   📁 Generated test file: {size_kb}KB")
        else:
            print(f"❌ Test generation failed: {result.stderr}")
            return False
            
    except Exception as e:
        print(f"❌ Error in test generation: {e}")
        return False
    
    # Summary
    print(f"\n🎉 AUTONOMOUS TEST COMPLETE!")
    print("=" * 50)
    print("✅ All pipeline components working")
    print("✅ Configuration loaded from .env")
    print("✅ Library extraction functional")
    print("✅ Gap analysis operational")
    if has_qobuz:
        print("✅ Qobuz validation working")
    print("✅ Test case generation working")
    
    print(f"\n📁 Test outputs generated:")
    outputs = [
        'scripts/test_library_sample.json',
        'scripts/test_sample.db', 
        'scripts/test_gaps.json',
        'scripts/test_validation.json' if has_qobuz else None,
        'scripts/test_generated_cases.cs'
    ]
    
    for output in outputs:
        if output and Path(output).exists():
            size = Path(output).stat().st_size
            print(f"   • {output} ({size:,} bytes)")
    
    print(f"\n🚀 Ready for full-scale analysis!")
    print("Next: python scripts/extract_lidarr_library.py --limit 500")
    
    return True

if __name__ == "__main__":
    success = asyncio.run(test_autonomous_analysis())
    sys.exit(0 if success else 1)