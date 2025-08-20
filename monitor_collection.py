#!/usr/bin/env python3
"""
Real-time monitoring dashboard for the continuous data collection

Shows live stats, progress, and estimates while collection is running.
"""

import sqlite3
import time
import json
import os
from datetime import datetime, timedelta
from typing import Dict, Any

def clear_screen():
    """Clear terminal screen"""
    os.system('cls' if os.name == 'nt' else 'clear')

def get_collection_stats() -> Dict[str, Any]:
    """Get current collection statistics from database"""
    if not os.path.exists("album_collection.db"):
        return {
            'total_albums': 0,
            'validated_albums': 0,
            'sources': {},
            'complexity_distribution': {},
            'quality_score': 0.0,
            'recent_rate': 0.0
        }
    
    conn = sqlite3.connect("album_collection.db")
    cursor = conn.cursor()
    
    try:
        # Total albums
        cursor.execute('SELECT COUNT(*) FROM albums')
        total_albums = cursor.fetchone()[0]
        
        # Validated albums
        cursor.execute('SELECT COUNT(*) FROM albums WHERE validated = TRUE')
        validated_albums = cursor.fetchone()[0]
        
        # Source distribution
        cursor.execute('SELECT source, COUNT(*) FROM albums GROUP BY source')
        sources = dict(cursor.fetchall())
        
        # Complexity distribution
        cursor.execute('''
            SELECT complexity_label, COUNT(*) 
            FROM albums 
            WHERE complexity_label != "" 
            GROUP BY complexity_label
        ''')
        complexity_dist = dict(cursor.fetchall())
        
        # Recent collection rate (last hour)
        one_hour_ago = (datetime.now() - timedelta(hours=1)).isoformat()
        cursor.execute('''
            SELECT COUNT(*) FROM albums 
            WHERE created_at > ?
        ''', (one_hour_ago,))
        recent_count = cursor.fetchone()[0]
        
        # Latest entries for freshness check
        cursor.execute('''
            SELECT created_at FROM albums 
            ORDER BY created_at DESC LIMIT 1
        ''')
        latest_result = cursor.fetchone()
        latest_timestamp = latest_result[0] if latest_result else None
        
        return {
            'total_albums': total_albums,
            'validated_albums': validated_albums,
            'sources': sources,
            'complexity_distribution': complexity_dist,
            'quality_score': validated_albums / max(total_albums, 1),
            'recent_rate': recent_count,
            'latest_timestamp': latest_timestamp
        }
        
    except Exception as e:
        print(f"Error reading database: {e}")
        return {}
    finally:
        conn.close()

def format_progress_bar(current: int, target: int, width: int = 50) -> str:
    """Create a visual progress bar"""
    if target <= 0:
        return "[" + " " * width + "] 0%"
    
    percentage = min(current / target, 1.0)
    filled = int(width * percentage)
    bar = "█" * filled + "░" * (width - filled)
    return f"[{bar}] {percentage:.1%}"

def estimate_completion_time(current: int, target: int, rate_per_hour: float) -> str:
    """Estimate completion time based on current rate"""
    if rate_per_hour <= 0 or current >= target:
        return "Unknown"
    
    remaining = target - current
    hours_remaining = remaining / rate_per_hour
    
    if hours_remaining < 1:
        minutes = hours_remaining * 60
        return f"{minutes:.0f} minutes"
    elif hours_remaining < 24:
        return f"{hours_remaining:.1f} hours"
    else:
        days = hours_remaining / 24
        return f"{days:.1f} days"

def display_dashboard(target_size: int = 10000, target_accuracy: float = 0.95):
    """Display the monitoring dashboard"""
    
    while True:
        try:
            clear_screen()
            
            # Header
            print("🎵 Qobuzarr ML Data Collection Monitor")
            print("=" * 60)
            print(f"📊 Target: {target_size:,} albums | 🎯 Accuracy: {target_accuracy:.1%}")
            print(f"⏰ {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
            print("=" * 60)
            print()
            
            # Get current stats
            stats = get_collection_stats()
            
            if not stats:
                print("❌ No data available. Is the collector running?")
                print("💡 Start collection with: python start_collection.py")
                time.sleep(5)
                continue
            
            current = stats['total_albums']
            
            # Progress section
            print("📈 COLLECTION PROGRESS")
            print("-" * 30)
            progress_bar = format_progress_bar(current, target_size)
            print(f"Albums:    {progress_bar}")
            print(f"Count:     {current:,} / {target_size:,}")
            print(f"Progress:  {current/target_size:.1%}")
            print()
            
            # Rate and timing
            rate = stats['recent_rate']
            print("⚡ COLLECTION RATE")
            print("-" * 20)
            print(f"Last hour: {rate} albums")
            print(f"Rate:      {rate:.1f} albums/hour")
            
            if rate > 0:
                eta = estimate_completion_time(current, target_size, rate)
                print(f"ETA:       {eta}")
            else:
                print("ETA:       Unknown (no recent activity)")
            print()
            
            # Data quality
            quality = stats['quality_score']
            print("📊 DATA QUALITY")
            print("-" * 20)
            print(f"Quality:   {quality:.1%}")
            print(f"Validated: {stats['validated_albums']:,}")
            
            # Latest activity
            latest = stats.get('latest_timestamp')
            if latest:
                try:
                    latest_dt = datetime.fromisoformat(latest)
                    time_ago = datetime.now() - latest_dt
                    if time_ago.total_seconds() < 300:  # 5 minutes
                        status = "🟢 ACTIVE"
                    elif time_ago.total_seconds() < 3600:  # 1 hour
                        status = "🟡 SLOW"
                    else:
                        status = "🔴 IDLE"
                    
                    print(f"Status:    {status}")
                    print(f"Last:      {time_ago.total_seconds()/60:.0f} minutes ago")
                except:
                    print("Status:    🟡 UNKNOWN")
            else:
                print("Status:    🔴 NO DATA")
            print()
            
            # Source breakdown
            sources = stats['sources']
            if sources:
                print("📡 DATA SOURCES")
                print("-" * 20)
                for source, count in sources.items():
                    percentage = count / current * 100 if current > 0 else 0
                    print(f"{source:12} {count:6,} ({percentage:4.1f}%)")
                print()
            
            # Complexity distribution
            complexity = stats['complexity_distribution']
            if complexity:
                print("🎚️  COMPLEXITY DISTRIBUTION")
                print("-" * 30)
                total_labeled = sum(complexity.values())
                for label, count in complexity.items():
                    percentage = count / total_labeled * 100 if total_labeled > 0 else 0
                    bar = "█" * int(percentage / 5)  # Scale to fit
                    print(f"{label:8} {count:6,} ({percentage:4.1f}%) {bar}")
                print()
            
            # Projections
            if rate > 0 and current < target_size:
                print("🔮 PROJECTIONS")
                print("-" * 20)
                
                # When will we reach target?
                hours_to_target = (target_size - current) / rate
                completion_date = datetime.now() + timedelta(hours=hours_to_target)
                print(f"Target:    {completion_date.strftime('%m/%d %H:%M')}")
                
                # Estimated model accuracy based on dataset size
                # Rough heuristic: accuracy improves with log of dataset size
                import math
                if current > 100:
                    estimated_acc = min(0.99, 0.6 + 0.15 * math.log10(current / 100))
                    print(f"Est. Acc:  {estimated_acc:.1%}")
                    
                    if estimated_acc >= target_accuracy:
                        print(f"🎯 Target accuracy likely achievable!")
                print()
            
            # Controls
            print("⌨️  CONTROLS")
            print("-" * 15)
            print("Ctrl+C: Exit monitor")
            print("Collection continues in background")
            print()
            print("🔄 Refreshing in 30 seconds...")
            
            # Wait for next update
            time.sleep(30)
            
        except KeyboardInterrupt:
            print("\n👋 Monitor stopped. Collection continues in background.")
            break
        except Exception as e:
            print(f"❌ Monitor error: {e}")
            time.sleep(10)

def main():
    """Main monitoring function"""
    import argparse
    
    parser = argparse.ArgumentParser(description="Monitor album data collection")
    parser.add_argument('--target', type=int, default=10000,
                       help='Target album count for progress tracking')
    parser.add_argument('--accuracy', type=float, default=0.95,
                       help='Target accuracy for progress tracking') 
    
    args = parser.parse_args()
    
    print("🚀 Starting collection monitor...")
    print("💡 Make sure data collection is running in another terminal")
    print("⏱️  Dashboard updates every 30 seconds")
    print()
    input("Press Enter to start monitoring...")
    
    display_dashboard(args.target, args.accuracy)

if __name__ == "__main__":
    main()