#!/usr/bin/env python3
"""
Environment configuration loader for library analysis scripts
"""

import os
from pathlib import Path
from typing import Optional

def load_env_file(env_path: str = ".env") -> bool:
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
    
    # Try to load .env file (check both scripts/.env and .env)
    if not load_env_file(".env"):
        load_env_file("scripts/.env")
    
    return {
        # Lidarr configuration
        'lidarr_url': os.getenv('LIDARR_URL', 'http://192.168.2.50:8686'),
        'lidarr_api_key': os.getenv('LIDARR_API_KEY', 'ca6a612bb8f84d9c976fcac967331da5'),
        
        # Qobuz configuration  
        'qobuz_app_id': os.getenv('QOBUZ_APP_ID'),
        'qobuz_app_secret': os.getenv('QOBUZ_APP_SECRET'),
        
        # Authentication methods (same as plugin)
        'qobuz_email': os.getenv('QOBUZ_EMAIL'),
        'qobuz_password': os.getenv('QOBUZ_PASSWORD'),  # MD5 hashed
        'qobuz_user_id': os.getenv('QOBUZ_USER_ID'),
        'qobuz_user_auth_token': os.getenv('QOBUZ_USER_AUTH_TOKEN'),
        
        # Analysis configuration
        'default_complexity_threshold': float(os.getenv('DEFAULT_COMPLEXITY_THRESHOLD', '0.4')),
        'default_extraction_limit': int(os.getenv('DEFAULT_EXTRACTION_LIMIT', '1000')),
        'default_gap_validation_limit': int(os.getenv('DEFAULT_GAP_VALIDATION_LIMIT', '50')),
        
        # Output configuration
        'analysis_output_dir': os.getenv('ANALYSIS_OUTPUT_DIR', 'scripts/analysis_results'),
        'generated_tests_dir': os.getenv('GENERATED_TESTS_DIR', 'tests/Qobuzarr.Tests/Unit/Indexers')
    }

def check_credentials() -> tuple[bool, bool, list[str]]:
    """Check which credentials are available (matching plugin authentication logic)"""
    
    config = get_config()
    
    # Check Lidarr credentials
    has_lidarr = bool(config['lidarr_url'] and config['lidarr_api_key'])
    
    # Check Qobuz app credentials
    has_qobuz_app = bool(config['qobuz_app_id'] and config['qobuz_app_secret'])
    
    # Check user authentication (following plugin's precedence logic)
    has_email_auth = bool(config['qobuz_email'] and config['qobuz_password'])
    has_token_auth = bool(config['qobuz_user_id'] and config['qobuz_user_auth_token'])
    
    # Email auth takes precedence over token auth (same as plugin)
    has_user_auth = has_email_auth or (has_token_auth and not has_email_auth)
    
    # Full Qobuz capability requires both app credentials and user auth
    has_qobuz = has_qobuz_app and has_user_auth
    
    # Generate status messages
    messages = []
    
    if has_lidarr:
        messages.append(f"OK Lidarr: {config['lidarr_url']}")
    else:
        messages.append("ERROR Lidarr: Missing URL or API key")
    
    if has_qobuz_app:
        messages.append(f"OK Qobuz App: ID {config['qobuz_app_id'][:8]}...")
    else:
        messages.append("ERROR Qobuz App: Missing app_id or app_secret")
    
    if has_email_auth:
        messages.append(f"OK Qobuz Auth: Email method ({config['qobuz_email']})")
    elif has_token_auth:
        messages.append(f"OK Qobuz Auth: Token method (user_id: {config['qobuz_user_id'][:8]}...)")
    else:
        messages.append("ERROR Qobuz Auth: Missing user credentials (email+password OR user_id+user_auth_token)")
    
    if has_qobuz:
        messages.append("READY Ready for full Qobuz validation!")
    elif has_qobuz_app:
        messages.append("WARNING Qobuz validation limited (missing user auth)")
    
    return has_lidarr, has_qobuz, messages

def get_qobuz_auth_method(config: dict) -> tuple[str, dict]:
    """Determine Qobuz authentication method and return auth params"""
    
    # Check for valid credentials (not placeholders)
    has_valid_email = (config['qobuz_email'] and 
                      config['qobuz_password'] and 
                      not config['qobuz_email'].startswith('your') and
                      not config['qobuz_password'].startswith('your'))
    
    has_valid_token = (config['qobuz_user_id'] and 
                      config['qobuz_user_auth_token'] and
                      not config['qobuz_user_id'].startswith('your') and
                      not config['qobuz_user_auth_token'].startswith('your'))
    
    # Email auth takes precedence (same as plugin logic) - but only if valid
    if has_valid_email:
        return 'email', {
            'username': config['qobuz_email'],
            'password': config['qobuz_password'],  # Should be MD5 hashed
            'app_id': config['qobuz_app_id']
        }
    
    # Token auth - if valid
    elif has_valid_token:
        return 'token', {
            'user_id': config['qobuz_user_id'],
            'user_auth_token': config['qobuz_user_auth_token'],
            'app_id': config['qobuz_app_id']
        }
    
    # App-only (limited access)
    elif config['qobuz_app_id']:
        return 'app_only', {
            'app_id': config['qobuz_app_id']
        }
    
    else:
        return 'none', {}

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
        print("\nSUCCESS Ready for full pipeline!")
    elif has_lidarr:
        print("\nWARNING Ready for extraction + analysis (no Qobuz validation)")
    else:
        print("\nERROR Missing credentials - check .env file")