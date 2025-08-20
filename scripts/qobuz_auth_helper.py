#!/usr/bin/env python3
"""
Qobuz Authentication Helper

Implements the exact authentication flow used by the plugin to get valid sessions.
"""

import aiohttp
import hashlib
import json
import asyncio
from typing import Optional, Tuple, Dict

class QobuzAuthenticator:
    """Handles Qobuz authentication exactly like the plugin"""
    
    def __init__(self, app_id: str, app_secret: str):
        self.app_id = app_id
        self.app_secret = app_secret
        self.base_url = "https://www.qobuz.com/api.json/0.2"
        self.session = None
        
    async def authenticate_with_email(self, email: str, password: str) -> Optional[Dict]:
        """Authenticate using email/password (password should be MD5 hashed)"""
        
        # Plugin uses: /user/login?app_id={appId}&email={email}&password={md5Password}
        url = f"{self.base_url}/user/login"
        params = {
            'app_id': self.app_id,
            'email': email,
            'password': password  # Should be MD5 hashed
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(url, params=params) as response:
                    if response.status == 200:
                        data = await response.json()
                        
                        if data.get('user_auth_token'):
                            self.session = {
                                'user_id': data['user']['id'],
                                'user_auth_token': data['user_auth_token'],
                                'app_id': self.app_id,
                                'subscription': data.get('user', {}).get('subscription', {})
                            }
                            print(f"SUCCESS: Authenticated as {data['user'].get('display_name', email)}")
                            return self.session
                        else:
                            print(f"ERROR: Authentication failed - no auth token received")
                            return None
                    else:
                        error_text = await response.text()
                        print(f"ERROR: Authentication failed - HTTP {response.status}: {error_text}")
                        return None
                        
        except Exception as e:
            print(f"ERROR: Authentication error: {e}")
            return None
    
    async def authenticate_with_token(self, user_id: str, user_auth_token: str) -> Optional[Dict]:
        """Authenticate using existing user_id and user_auth_token"""
        
        # Create session and validate it (like plugin does)
        self.session = {
            'user_id': user_id,
            'user_auth_token': user_auth_token,
            'app_id': self.app_id
        }
        
        # Validate session by making test API call
        if await self.validate_session():
            print(f"SUCCESS: Token authentication validated for user {user_id}")
            return self.session
        else:
            print(f"ERROR: Invalid user_id or user_auth_token")
            self.session = None
            return None
    
    async def validate_session(self) -> bool:
        """Validate session by making test API call (like plugin does)"""
        
        if not self.session:
            return False
        
        # Plugin validates with: /user/login?app_id={appId}&user_auth_token={authToken}
        url = f"{self.base_url}/user/login"
        params = {
            'app_id': self.session['app_id'],
            'user_auth_token': self.session['user_auth_token']
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(url, params=params) as response:
                    return response.status == 200
        except:
            return False
    
    def get_search_params(self) -> Dict[str, str]:
        """Get parameters for search requests (matching plugin format)"""
        
        if not self.session:
            raise ValueError("Not authenticated - call authenticate_with_email or authenticate_with_token first")
        
        # Plugin uses: app_id={session.AppId}&user_auth_token={session.AuthToken}&country_code=CA
        return {
            'app_id': self.session['app_id'],
            'user_auth_token': self.session['user_auth_token'],
            'country_code': 'CA'
        }

async def test_authentication():
    """Test authentication flow"""
    
    from load_env import get_config, get_qobuz_auth_method
    
    config = get_config()
    auth_method, auth_params = get_qobuz_auth_method(config)
    
    if auth_method == 'none':
        print("ERROR: No Qobuz credentials configured")
        return None
    
    authenticator = QobuzAuthenticator(
        config['qobuz_app_id'],
        config['qobuz_app_secret']
    )
    
    print(f"Testing {auth_method} authentication...")
    
    session = None
    
    if auth_method == 'email':
        session = await authenticator.authenticate_with_email(
            auth_params['username'],
            auth_params['password']
        )
    elif auth_method == 'token':
        session = await authenticator.authenticate_with_token(
            auth_params['user_id'],
            auth_params['user_auth_token']
        )
    
    if session:
        print("Authentication successful!")
        print(f"Session: {session}")
        
        # Test a simple search
        search_params = authenticator.get_search_params()
        search_params['query'] = 'test'
        search_params['limit'] = '5'
        
        url = f"{authenticator.base_url}/album/search"
        
        try:
            async with aiohttp.ClientSession() as http_session:
                async with http_session.get(url, params=search_params) as response:
                    print(f"Test search result: HTTP {response.status}")
                    if response.status == 200:
                        data = await response.json()
                        album_count = len(data.get('albums', {}).get('items', []))
                        print(f"SUCCESS: Found {album_count} albums for 'test' query")
                        return True
                    else:
                        error_text = await response.text()
                        print(f"Search failed: {error_text}")
                        return False
        except Exception as e:
            print(f"Search error: {e}")
            return False
    else:
        print("Authentication failed")
        return False

if __name__ == "__main__":
    success = asyncio.run(test_authentication())
    if success:
        print("Ready for gap validation!")
    else:
        print("Fix authentication before proceeding")