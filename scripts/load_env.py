#!/usr/bin/env python3
"""
Environment configuration loader for library analysis scripts
"""

import os
from pathlib import Path
from typing import Optional

def load_env_file(env_path: str = "scripts/.env") -> bool:
    """Load environment variables from .env file"""
    
    env_file = Path(env_path)
    if not env_file.exists():
        return False
    
    try:
        with open(env_file, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                
                # Skip comments and empty lines
                if not line or line.startswith('#'):
                    continue
                
                # Parse KEY=VALUE
                if '=' in line:
                    key, value = line.split('=', 1)
                    key = key.strip()
                    value = value.strip()
                    
                    # Remove quotes if present
                    if value.startswith('"') and value.endswith('"'):
                        value = value[1:-1]
                    elif value.startswith("'") and value.endswith("'"):
                        value = value[1:-1]
                    
                    # Set environment variable
                    os.environ[key] = value
        
        return True
        
    except Exception as e:
        print(f"Error loading .env file: {e}")
        return False

def get_config() -> dict:
    """Get configuration from environment variables with defaults"""
    
    # Try to load .env file
    load_env_file()
    
    return {
        # Lidarr configuration
        'lidarr_url': os.getenv('LIDARR_URL', 'http://192.168.2.50:8686'),
        'lidarr_api_key': os.getenv('LIDARR_API_KEY', 'ca6a612bb8f84d9c976fcac967331da5'),
        
        # Qobuz configuration
        'qobuz_app_id': os.getenv('QOBUZ_APP_ID'),
        'qobuz_app_secret': os.getenv('QOBUZ_APP_SECRET'),
        'qobuz_email': os.getenv('QOBUZ_EMAIL'),
        'qobuz_password': os.getenv('QOBUZ_PASSWORD'),
        'qobuz_user_token': os.getenv('QOBUZ_USER_TOKEN'),
        
        # Analysis configuration
        'default_complexity_threshold': float(os.getenv('DEFAULT_COMPLEXITY_THRESHOLD', '0.4')),
        'default_extraction_limit': int(os.getenv('DEFAULT_EXTRACTION_LIMIT', '1000')),
        'default_gap_validation_limit': int(os.getenv('DEFAULT_GAP_VALIDATION_LIMIT', '50')),
        
        # Output configuration
        'analysis_output_dir': os.getenv('ANALYSIS_OUTPUT_DIR', 'scripts/analysis_results'),
        'generated_tests_dir': os.getenv('GENERATED_TESTS_DIR', 'tests/Qobuzarr.Tests/Unit/Indexers')
    }

def check_credentials() -> tuple[bool, bool, list[str]]:
    """Check which credentials are available"""
    
    config = get_config()
    
    # Check Lidarr credentials
    has_lidarr = bool(config['lidarr_url'] and config['lidarr_api_key'])
    
    # Check Qobuz credentials  
    has_qobuz = bool(config['qobuz_app_id'] and config['qobuz_app_secret'])
    
    # Generate status messages
    messages = []
    
    if has_lidarr:
        messages.append(f"✅ Lidarr: {config['lidarr_url']}")
    else:
        messages.append("❌ Lidarr: Missing URL or API key")
    
    if has_qobuz:
        messages.append(f"✅ Qobuz: App ID {config['qobuz_app_id'][:8]}...")
        if config['qobuz_user_token']:
            messages.append("✅ Qobuz: User token available")
    else:
        messages.append("❌ Qobuz: Missing app credentials (validation will be skipped)")
    
    return has_lidarr, has_qobuz, messages

if __name__ == "__main__":
    """Test configuration loading"""
    
    print("Configuration Test")
    print("=" * 30)
    
    has_lidarr, has_qobuz, messages = check_credentials()
    
    for message in messages:
        print(message)
    
    config = get_config()
    print(f"\nAnalysis Configuration:")
    print(f"  Complexity threshold: {config['default_complexity_threshold']}")
    print(f"  Extraction limit: {config['default_extraction_limit']}")
    print(f"  Validation limit: {config['default_gap_validation_limit']}")
    
    if has_lidarr and has_qobuz:
        print("\n🎉 Ready for full pipeline!")
    elif has_lidarr:
        print("\n⚠️ Ready for extraction + analysis (no Qobuz validation)")
    else:
        print("\n❌ Missing credentials - check .env file")