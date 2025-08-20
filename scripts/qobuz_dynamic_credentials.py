#!/usr/bin/env python3
"""
Qobuz Dynamic Credential Fetcher

Implements the exact same dynamic credential fetching logic as the plugin,
using QobuzApiSharp's proven method to extract app_id and app_secret from
the Qobuz web player bundle.js file.
"""

import aiohttp
import re
import hashlib
import asyncio
from typing import Optional, Tuple

class QobuzDynamicCredentialFetcher:
    """Fetches app credentials dynamically from Qobuz web player (exactly like plugin)"""
    
    def __init__(self):
        self.user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
        
    async def get_dynamic_credentials(self) -> Tuple[Optional[str], Optional[str]]:
        """
        Fetch app_id and app_secret dynamically from Qobuz web player
        Uses the exact same method as QobuzApiSharp and TrevTV's plugin
        """
        
        try:
            print("INFO: Fetching dynamic credentials from Qobuz web player...")
            
            # Step 1: Fetch the login page to get bundle.js URL
            login_url = "https://play.qobuz.com/login"
            
            async with aiohttp.ClientSession() as session:
                async with session.get(login_url, headers={'User-Agent': self.user_agent}) as response:
                    if response.status != 200:
                        print(f"ERROR: Failed to fetch Qobuz login page: HTTP {response.status}")
                        return None, None
                    
                    login_html = await response.text()
            
            # Step 2: Extract bundle.js URL using QobuzApiSharp's regex pattern
            bundle_pattern = r'<script src="(?P<bundleJS>\/resources\/\d+\.\d+\.\d+-[a-z]\d{3}\/bundle\.js)'
            bundle_match = re.search(bundle_pattern, login_html)
            
            if not bundle_match:
                print("ERROR: Failed to find bundle.js link in Qobuz web player")
                return None, None
            
            bundle_suffix = bundle_match.group('bundleJS')
            bundle_url = f"https://play.qobuz.com{bundle_suffix}"
            
            print(f"INFO: Found bundle.js URL: {bundle_url}")
            
            # Step 3: Fetch bundle.js
            async with aiohttp.ClientSession() as session:
                async with session.get(bundle_url, headers={'User-Agent': self.user_agent}) as response:
                    if response.status != 200:
                        print(f"ERROR: Failed to fetch bundle.js: HTTP {response.status}")
                        return None, None
                    
                    bundle_content = await response.text()
            
            # Step 4: Extract App ID using QobuzApiSharp's regex pattern
            app_id_pattern = r'production:\{api:\{appId:"(?P<appID>.*?)",appSecret:'
            app_id_match = re.search(app_id_pattern, bundle_content)
            
            if not app_id_match:
                print("ERROR: Failed to find production app_id in bundle.js")
                return None, None
            
            app_id = app_id_match.group('appID')
            
            # Step 5: Extract App Secret using QobuzApiSharp's complex method
            app_secret = self.extract_app_secret_from_bundle(bundle_content)
            
            if not app_secret:
                print("ERROR: Failed to extract app_secret from bundle.js")
                return None, None
            
            print(f"SUCCESS: Dynamic credentials extracted - App ID: {app_id}")
            return app_id, app_secret
            
        except Exception as e:
            print(f"ERROR: Dynamic credential fetching failed: {e}")
            return None, None
    
    def extract_app_secret_from_bundle(self, bundle_content: str) -> Optional[str]:
        """
        Extract App Secret from bundle.js using QobuzApiSharp's complex algorithm
        This is the EXACT same logic as the plugin's ExtractAppSecretFromBundle method
        """
        
        try:
            # Step 1: Find seed and timezone pattern (QobuzApiSharp's exact regex)
            seed_timezone_pattern = r'\):[a-z]\.initialSeed\("(?P<seed>.*?)",window\.utimezone\.(?P<timezone>[a-z]+)\)'
            seed_match = re.search(seed_timezone_pattern, bundle_content)
            
            if not seed_match:
                print("ERROR: Failed to find seed and timezone in bundle.js")
                return None
            
            seed = seed_match.group('seed')
            production_timezone = seed_match.group('timezone').title()
            
            print(f"INFO: Found seed: {seed}, timezone: {production_timezone}")
            
            # Step 2: Find info and extras for the production timezone (EXACT plugin pattern)
            # Plugin uses: timezones:\[.*?name:".*?\/{timezone}\\",info:\\"(?<info>.*?)\\",extras:\\"(?<extras>.*?)\\"
            info_extras_pattern = f'timezones:\\[.*?name:".*?\\/{production_timezone}\\\\",info:\\\\"(?P<info>.*?)\\\\",extras:\\\\"(?P<extras>.*?)\\\\"'
            info_match = re.search(info_extras_pattern, bundle_content, re.DOTALL)
            
            if not info_match:
                # Try alternate pattern - sometimes the escaping is different
                alt_pattern = f'timezones:\\[.*?name:".*?/{production_timezone}",info:"(?P<info>.*?)",extras:"(?P<extras>.*?)"'
                info_match = re.search(alt_pattern, bundle_content, re.DOTALL)
                
                if not info_match:
                    print(f"ERROR: Failed to find info and extras for timezone {production_timezone}")
                    # Debug: try to find what timezones are available
                    timezone_pattern = r'timezones:\[.*?name:".*?/([^"\\]+)'
                    available_timezones = re.findall(timezone_pattern, bundle_content)
                    print(f"DEBUG: Available timezones found: {available_timezones[:10]}")
                    
                    # Try to find the actual timezone structure
                    timezone_structure_pattern = r'timezones:\[(.*?)\]'
                    structure_match = re.search(timezone_structure_pattern, bundle_content, re.DOTALL)
                    if structure_match:
                        structure = structure_match.group(1)[:500]  # First 500 chars for debug
                        print(f"DEBUG: Timezone structure sample: {structure}")
                    
                    return None
            
            info = info_match.group('info')
            extras = info_match.group('extras')
            
            print(f"INFO: Found info: {info}, extras: {extras}")
            
            # Step 3: Concatenate seed, info, and extras (EXACT plugin algorithm)
            base64_encoded_app_secret = seed + info + extras
            
            # Step 4: Remove last 44 characters (EXACT plugin logic)
            if len(base64_encoded_app_secret) <= 44:
                print("ERROR: Concatenated seed+info+extras string is too short")
                return None
            
            base64_encoded_app_secret = base64_encoded_app_secret[:-44]  # Remove last 44 chars
            
            # Step 5: Base64 decode to get app secret bytes
            import base64
            try:
                decoded_app_secret_bytes = base64.b64decode(base64_encoded_app_secret)
            except Exception as e:
                print(f"ERROR: Base64 decode failed: {e}")
                return None
            
            # Step 6: UTF-8 decode to get final app secret string
            app_secret = decoded_app_secret_bytes.decode('utf-8')
            
            print(f"INFO: Successfully extracted app_secret using QobuzApiSharp algorithm")
            return app_secret
            
        except Exception as e:
            print(f"ERROR: App secret extraction failed: {e}")
            return None

async def fetch_and_save_credentials(save_to_env: bool = True) -> Tuple[Optional[str], Optional[str]]:
    """Fetch dynamic credentials and optionally save to .env file"""
    
    fetcher = QobuzDynamicCredentialFetcher()
    app_id, app_secret = await fetcher.get_dynamic_credentials()
    
    if app_id and app_secret and save_to_env:
        # Update .env file with dynamic credentials
        try:
            # Read current .env
            env_content = ""
            if os.path.exists('.env'):
                with open('.env', 'r', encoding='utf-8') as f:
                    env_content = f.read()
            
            # Update or add credentials
            lines = env_content.split('\n')
            updated_lines = []
            app_id_updated = False
            app_secret_updated = False
            
            for line in lines:
                if line.startswith('QOBUZ_APP_ID='):
                    updated_lines.append(f'QOBUZ_APP_ID={app_id}')
                    app_id_updated = True
                elif line.startswith('QOBUZ_APP_SECRET='):
                    updated_lines.append(f'QOBUZ_APP_SECRET={app_secret}')
                    app_secret_updated = True
                else:
                    updated_lines.append(line)
            
            # Add if not found
            if not app_id_updated:
                updated_lines.append(f'QOBUZ_APP_ID={app_id}')
            if not app_secret_updated:
                updated_lines.append(f'QOBUZ_APP_SECRET={app_secret}')
            
            # Write back
            with open('.env', 'w', encoding='utf-8') as f:
                f.write('\n'.join(updated_lines))
            
            print(f"SUCCESS: Dynamic credentials saved to .env file")
            
        except Exception as e:
            print(f"WARNING: Could not save to .env file: {e}")
    
    return app_id, app_secret

async def main():
    """Test dynamic credential fetching"""
    
    print("Qobuz Dynamic Credential Fetcher")
    print("=" * 40)
    print("Using the same method as your plugin...")
    print()
    
    app_id, app_secret = await fetch_and_save_credentials(save_to_env=True)
    
    if app_id and app_secret:
        print()
        print("SUCCESS: Dynamic credentials fetched!")
        print(f"App ID: {app_id}")
        print(f"App Secret: {app_secret[:8]}...")
        print()
        print("These credentials are now saved to your .env file")
        print("Ready to run gap validation!")
    else:
        print()
        print("ERROR: Failed to fetch dynamic credentials")
        print("This might indicate Qobuz has changed their web player structure")

if __name__ == "__main__":
    import os
    asyncio.run(main())