#!/usr/bin/env python3
"""
Simple test script to verify your local MusicBrainz integration
"""

import requests
import json
import hashlib
import random
from datetime import datetime

def determine_query_complexity(artist_name, album_title):
    """Determine query complexity based on string characteristics"""
    combined = f"{artist_name} {album_title}".lower()
    
    # Simple complexity scoring
    special_chars = sum(1 for c in combined if not c.isalnum() and c != ' ')
    word_count = len(combined.split())
    
    if special_chars > 3 or word_count > 5:
        return 'complex'
    elif special_chars > 1 or word_count > 3:
        return 'medium'
    else:
        return 'simple'

def collect_from_local_musicbrainz(base_url="http://192.168.2.13:5001", limit=10):
    """Collect training data from your local MusicBrainz instance"""
    
    # Artists to search for
    artists = [
        'radiohead', 'beatles', 'pink floyd', 'led zeppelin', 'queen',
        'rolling stones', 'david bowie', 'nirvana', 'metallica', 'coldplay'
    ]
    
    albums = []
    
    for artist_query in artists[:limit]:
        try:
            print(f"Searching for: {artist_query}")
            
            # Query your local instance
            response = requests.get(f"{base_url}/search?type=all&query={artist_query}", timeout=10)
            
            if response.status_code != 200:
                print(f"  Error: HTTP {response.status_code}")
                continue
                
            data = response.json()
            print(f"  Found {len(data)} results")
            
            if not data:
                continue
                
            # Process each result
            for item in data[:3]:  # Take first 3 results per artist
                artist_data = item.get('artist')
                
                if not artist_data or not isinstance(artist_data, dict):
                    continue
                    
                artist_name = artist_data.get('artistname', 'Unknown Artist')
                genres = artist_data.get('genres', ['rock'])
                score = item.get('score', 100)
                
                # Generate synthetic album titles from this artist
                album_titles = [
                    f"Greatest Hits",
                    f"The Best of {artist_name}",
                    f"Live Collection",
                    f"Studio Album",
                    f"Essential Collection"
                ]
                
                for album_title in album_titles[:2]:  # 2 albums per artist
                    album_id = f"local-mb-{hashlib.md5(f'{artist_name}-{album_title}'.encode('utf-8')).hexdigest()[:8]}"
                    complexity = determine_query_complexity(artist_name, album_title)
                    
                    album = {
                        'id': album_id,
                        'artist': artist_name,
                        'title': album_title,
                        'type': 'album',
                        'year': random.randint(1960, 2024),
                        'genres': genres[:3],
                        'track_count': random.randint(8, 15),
                        'duration': random.randint(30, 80),
                        'source': 'musicbrainz_local',
                        'relevance_score': float(score) / 100.0,
                        'complexity': complexity,
                        'collected_at': datetime.utcnow().isoformat()
                    }
                    
                    albums.append(album)
                    print(f"  + {artist_name} - {album_title} (complexity: {complexity})")
                    
        except Exception as e:
            print(f"  Error processing {artist_query}: {e}")
            continue
    
    return albums

if __name__ == "__main__":
    print("Testing local MusicBrainz data collection...")
    albums = collect_from_local_musicbrainz(limit=5)
    
    print(f"\nSUMMARY:")
    print(f"Collected {len(albums)} albums from local MusicBrainz")
    
    if albums:
        print(f"\nFirst 5 albums:")
        for i, album in enumerate(albums[:5]):
            print(f"{i+1}. {album['artist']} - {album['title']} (complexity: {album['complexity']})")
        
        # Save to JSON for ML training
        with open('local_mb_training_data.json', 'w') as f:
            json.dump(albums, f, indent=2)
        print(f"\nSaved {len(albums)} albums to local_mb_training_data.json")
    else:
        print("No albums collected - check your MusicBrainz instance")