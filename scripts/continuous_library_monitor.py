#!/usr/bin/env python3
"""
Continuous Library Monitor

Monitors your Lidarr library for new albums and automatically tests them against
the Unicode query builder to identify new edge cases and failure patterns.

Usage:
    python scripts/continuous_library_monitor.py --daemon
    python scripts/continuous_library_monitor.py --check-once
    python scripts/continuous_library_monitor.py --initial-scan
"""

import asyncio
import aiohttp
import json
import sqlite3
import argparse
from datetime import datetime, timedelta
from pathlib import Path
import logging
import os
import time
from typing import List, Dict, Optional

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class LibraryChangeMonitor:
    """Monitors Lidarr library for new albums and changes"""
    
    def __init__(self, lidarr_url: str, api_key: str, database_path: str = "scripts/library_monitor.db"):
        self.lidarr_url = lidarr_url.rstrip('/')
        self.api_key = api_key
        self.database_path = database_path
        self.setup_database()
        
    def setup_database(self):
        """Initialize monitoring database"""
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        # Table for tracking library state
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS library_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lidarr_id INTEGER,
                artist TEXT,
                album TEXT,
                date_added TIMESTAMP,
                first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                complexity_score REAL,
                unicode_test_status TEXT,
                gap_analysis_completed BOOLEAN DEFAULT FALSE,
                UNIQUE(lidarr_id)
            )
        ''')
        
        # Table for tracking new gaps discovered
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS discovered_gaps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lidarr_id INTEGER,
                artist TEXT,
                album TEXT,
                gap_type TEXT,
                discovery_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                validated_against_qobuz BOOLEAN DEFAULT FALSE,
                gap_confirmed BOOLEAN DEFAULT NULL,
                working_manual_query TEXT,
                recommended_fixes TEXT
            )
        ''')
        
        # Table for monitoring stats
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS monitoring_stats (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                check_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                albums_checked INTEGER,
                new_albums_found INTEGER,
                new_gaps_discovered INTEGER,
                unicode_success_rate REAL,
                total_library_size INTEGER
            )
        ''')
        
        conn.commit()
        conn.close()
        logger.info(f"📊 Monitoring database initialized: {self.database_path}")
    
    async def check_for_new_albums(self) -> List[Dict]:
        """Check Lidarr for albums added since last check"""
        
        logger.info("🔍 Checking for new albums in Lidarr library...")
        
        # Get last check time
        last_check = self.get_last_check_time()
        
        new_albums = []
        
        async with aiohttp.ClientSession() as session:
            # Get recent album additions
            url = f"{self.lidarr_url}/api/v1/album"
            params = {
                'apikey': self.api_key,
                'sortKey': 'dateAdded',
                'sortDirection': 'descending',
                'pageSize': 100
            }
            
            try:
                async with session.get(url, params=params) as response:
                    if response.status != 200:
                        logger.error(f"❌ Lidarr API error: {response.status}")
                        return []
                    
                    albums_data = await response.json()
                    
                    for album_data in albums_data:
                        date_added = album_data.get('dateAdded')
                        if date_added and date_added > last_check:
                            new_albums.append(album_data)
                        else:
                            # Since we're sorting by dateAdded desc, we can break here
                            break
                    
            except Exception as e:
                logger.error(f"❌ Error checking for new albums: {e}")
                return []
        
        logger.info(f"📈 Found {len(new_albums)} new albums since {last_check}")
        return new_albums
    
    def get_last_check_time(self) -> str:
        """Get the timestamp of the last monitoring check"""
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        cursor.execute('SELECT MAX(check_date) FROM monitoring_stats')
        result = cursor.fetchone()
        
        conn.close()
        
        if result[0]:
            return result[0]
        else:
            # First run - check last 30 days
            return (datetime.now() - timedelta(days=30)).isoformat()
    
    async def analyze_new_albums(self, new_albums: List[Dict]) -> Dict:
        """Analyze new albums for complexity and potential gaps"""
        
        if not new_albums:
            return {
                'new_albums_count': 0,
                'new_gaps_discovered': 0,
                'unicode_success_rate': 1.0
            }
        
        logger.info(f"🧪 Analyzing {len(new_albums)} new albums for Unicode gaps...")
        
        # Import our analysis tools
        import sys
        sys.path.append('scripts')
        
        try:
            from analyze_unicode_gaps import GapAnalyzer, UnicodeQuerySimulator
        except ImportError:
            logger.error("❌ Could not import gap analysis tools")
            return {'error': 'Analysis tools not available'}
        
        gap_analyzer = GapAnalyzer()
        new_gaps = []
        unicode_successes = 0
        
        for album_data in new_albums:
            try:
                # Process album data
                artist = album_data.get('artist', {}).get('artistName', 'Unknown')
                album_title = album_data.get('title', 'Unknown')
                lidarr_id = album_data.get('id', 0)
                
                # Create album dict for analysis
                album_dict = {
                    'artist': artist,
                    'album': album_title,
                    'lidarr_id': lidarr_id,
                    'complexity_score': 0.5,  # Will be calculated
                    'complexity_factors': '[]'
                }
                
                # Test for gaps
                gap = gap_analyzer.predict_search_gap(album_dict, 
                    gap_analyzer.unicode_simulator.simulate_generate_query_variants(artist, album_title))
                
                if gap:
                    new_gaps.append(gap)
                    logger.info(f"🔍 New gap discovered: {artist} - {album_title}")
                else:
                    unicode_successes += 1
                
                # Store in database
                self.store_album_analysis(album_dict, gap is not None)
                
            except Exception as e:
                logger.warning(f"⚠️ Error analyzing {album_data}: {e}")
                continue
        
        # Update monitoring stats
        unicode_success_rate = unicode_successes / len(new_albums) if new_albums else 1.0
        self.update_monitoring_stats(len(new_albums), len(new_gaps), unicode_success_rate)
        
        return {
            'new_albums_count': len(new_albums),
            'new_gaps_discovered': len(new_gaps),
            'unicode_success_rate': unicode_success_rate,
            'gaps': new_gaps
        }
    
    def store_album_analysis(self, album_data: Dict, has_gap: bool):
        """Store album analysis in monitoring database"""
        
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        cursor.execute('''
            INSERT OR REPLACE INTO library_snapshots
            (lidarr_id, artist, album, date_added, complexity_score, unicode_test_status)
            VALUES (?, ?, ?, ?, ?, ?)
        ''', (
            album_data['lidarr_id'],
            album_data['artist'],
            album_data['album'],
            datetime.now().isoformat(),
            album_data.get('complexity_score', 0),
            'gap_detected' if has_gap else 'unicode_success'
        ))
        
        conn.commit()
        conn.close()
    
    def update_monitoring_stats(self, albums_checked: int, new_gaps: int, success_rate: float):
        """Update monitoring statistics"""
        
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        # Get total library size
        cursor.execute('SELECT COUNT(*) FROM library_snapshots')
        total_size = cursor.fetchone()[0]
        
        cursor.execute('''
            INSERT INTO monitoring_stats
            (albums_checked, new_albums_found, new_gaps_discovered, unicode_success_rate, total_library_size)
            VALUES (?, ?, ?, ?, ?)
        ''', (albums_checked, albums_checked, new_gaps, success_rate, total_size))
        
        conn.commit()
        conn.close()
    
    def generate_monitoring_report(self) -> Dict:
        """Generate monitoring summary report"""
        
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        # Get recent stats (last 30 days)
        cursor.execute('''
            SELECT * FROM monitoring_stats 
            WHERE check_date >= datetime('now', '-30 days')
            ORDER BY check_date DESC
        ''')
        
        recent_stats = cursor.fetchall()
        
        # Get gap trends
        cursor.execute('''
            SELECT gap_type, COUNT(*) as count
            FROM discovered_gaps 
            WHERE discovery_date >= datetime('now', '-30 days')
            GROUP BY gap_type
            ORDER BY count DESC
        ''')
        
        gap_trends = dict(cursor.fetchall())
        
        # Get unicode success trends
        cursor.execute('''
            SELECT DATE(check_date) as date, AVG(unicode_success_rate) as avg_success_rate
            FROM monitoring_stats
            WHERE check_date >= datetime('now', '-7 days')
            GROUP BY DATE(check_date)
            ORDER BY date
        ''')
        
        success_trends = cursor.fetchall()
        
        conn.close()
        
        return {
            'monitoring_period': '30 days',
            'total_checks': len(recent_stats),
            'gap_trends': gap_trends,
            'success_rate_trend': success_trends,
            'avg_success_rate': sum(stat[4] for stat in recent_stats) / len(recent_stats) if recent_stats else 1.0,
            'recent_activity': recent_stats[:5]  # Last 5 checks
        }

class ContinuousMonitor:
    """Main continuous monitoring orchestrator"""
    
    def __init__(self, lidarr_url: str, api_key: str):
        self.monitor = LibraryChangeMonitor(lidarr_url, api_key)
        
    async def run_single_check(self) -> Dict:
        """Run a single monitoring check"""
        
        logger.info("🔄 Running single library check...")
        
        # Check for new albums
        new_albums = await self.monitor.check_for_new_albums()
        
        # Analyze new albums
        analysis_results = await self.monitor.analyze_new_albums(new_albums)
        
        # Generate report
        monitoring_report = self.monitor.generate_monitoring_report()
        
        return {
            'check_timestamp': datetime.now().isoformat(),
            'new_albums_analysis': analysis_results,
            'monitoring_report': monitoring_report
        }
    
    async def run_daemon_mode(self, check_interval_hours: int = 6):
        """Run continuous monitoring in daemon mode"""
        
        logger.info(f"🔄 Starting continuous monitoring (checking every {check_interval_hours} hours)")
        
        while True:
            try:
                result = await self.run_single_check()
                
                # Log summary
                analysis = result['new_albums_analysis']
                logger.info(f"📊 Check complete: {analysis['new_albums_count']} new albums, "
                           f"{analysis['new_gaps_discovered']} new gaps, "
                           f"{analysis['unicode_success_rate']:.1%} success rate")
                
                # Wait for next check
                sleep_seconds = check_interval_hours * 3600
                logger.info(f"😴 Sleeping for {check_interval_hours} hours until next check...")
                await asyncio.sleep(sleep_seconds)
                
            except KeyboardInterrupt:
                logger.info("🛑 Monitoring stopped by user")
                break
            except Exception as e:
                logger.error(f"❌ Monitoring error: {e}")
                # Wait shorter time on error
                await asyncio.sleep(300)  # 5 minutes
    
    async def run_initial_scan(self):
        """Run initial full library scan to establish baseline"""
        
        logger.info("🔍 Running initial full library scan...")
        
        # Use existing extraction tool
        import subprocess
        import sys
        
        try:
            # Run library extraction
            result = subprocess.run([
                sys.executable, 'scripts/extract_lidarr_library.py',
                '--complexity-threshold', '0.2',  # Lower threshold for initial scan
                '--limit', '2000',  # Reasonable limit for initial scan
                '--output', 'scripts/initial_library_baseline'
            ], capture_output=True, text=True)
            
            if result.returncode == 0:
                logger.info("✅ Initial library extraction completed")
                
                # Run gap analysis
                gap_result = subprocess.run([
                    sys.executable, 'scripts/analyze_unicode_gaps.py',
                    '--database', 'scripts/initial_library_baseline.db',
                    '--min-complexity', '0.3',
                    '--output', 'scripts/initial_gaps_baseline.json'
                ], capture_output=True, text=True)
                
                if gap_result.returncode == 0:
                    logger.info("✅ Initial gap analysis completed")
                    print("🎯 Initial scan complete! Check scripts/initial_gaps_baseline.json for results")
                else:
                    logger.error(f"❌ Gap analysis failed: {gap_result.stderr}")
            else:
                logger.error(f"❌ Library extraction failed: {result.stderr}")
                
        except Exception as e:
            logger.error(f"❌ Initial scan failed: {e}")

async def main():
    """Main monitoring workflow"""
    parser = argparse.ArgumentParser(description="Continuous library monitoring for Unicode gaps")
    parser.add_argument('--lidarr-url', default='http://192.168.2.50:8686',
                       help='Lidarr instance URL')
    parser.add_argument('--api-key', default='ca6a612bb8f84d9c976fcac967331da5',
                       help='Lidarr API key')
    parser.add_argument('--daemon', action='store_true',
                       help='Run in continuous daemon mode')
    parser.add_argument('--check-once', action='store_true',
                       help='Run single check and exit')
    parser.add_argument('--initial-scan', action='store_true',
                       help='Run initial full library scan')
    parser.add_argument('--check-interval', type=int, default=6,
                       help='Hours between checks in daemon mode')
    parser.add_argument('--output', default='scripts/monitoring_results.json',
                       help='Output file for monitoring results')
    
    args = parser.parse_args()
    
    print("📡 Continuous Library Monitor")
    print("=" * 50)
    print(f"🏠 Lidarr: {args.lidarr_url}")
    print(f"🔑 API Key: {args.api_key[:8]}...")
    
    if args.daemon:
        print(f"🔄 Mode: Continuous (every {args.check_interval} hours)")
    elif args.check_once:
        print("🔄 Mode: Single check")
    elif args.initial_scan:
        print("🔄 Mode: Initial full scan")
    else:
        print("🔄 Mode: Single check (default)")
    
    print("=" * 50)
    
    monitor = ContinuousMonitor(args.lidarr_url, args.api_key)
    
    try:
        if args.initial_scan:
            await monitor.run_initial_scan()
            
        elif args.daemon:
            await monitor.run_daemon_mode(args.check_interval)
            
        else:  # Single check or check-once
            result = await monitor.run_single_check()
            
            # Save results
            with open(args.output, 'w', encoding='utf-8') as f:
                json.dump(result, f, indent=2, ensure_ascii=False)
            
            # Display summary
            analysis = result['new_albums_analysis']
            monitoring = result['monitoring_report']
            
            print(f"\n📊 MONITORING RESULTS")
            print("=" * 50)
            print(f"🆕 New albums found: {analysis['new_albums_count']}")
            print(f"🔍 New gaps discovered: {analysis['new_gaps_discovered']}")
            print(f"✅ Unicode success rate: {analysis['unicode_success_rate']:.1%}")
            print(f"📈 Average success rate (30 days): {monitoring['avg_success_rate']:.1%}")
            
            if analysis['new_gaps_discovered'] > 0:
                print(f"\n🎯 ACTION REQUIRED:")
                print("New gaps detected! Consider running:")
                print("1. python scripts/validate_qobuz_gaps.py (validate against Qobuz)")
                print("2. python scripts/generate_complex_test_cases.py (update test suite)")
            
            print(f"\n💾 Results saved to: {args.output}")
        
    except KeyboardInterrupt:
        logger.info("🛑 Monitoring stopped by user")
    except Exception as e:
        logger.error(f"💥 Monitoring failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())