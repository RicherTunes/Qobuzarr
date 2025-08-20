#!/usr/bin/env python3
"""
Generate comprehensive ML training dataset from local MusicBrainz instance
Creates diverse complexity examples for better model training
"""

import requests
import json
import hashlib
import random
from datetime import datetime, UTC
from typing import Dict, List, Any

def determine_query_complexity(artist_name: str, album_title: str) -> str:
    """Determine query complexity based on string characteristics"""
    combined = f"{artist_name} {album_title}".lower()
    
    # Count complexity indicators
    special_chars = sum(1 for c in combined if not c.isalnum() and c != ' ')
    word_count = len(combined.split())
    parentheses = combined.count('(') + combined.count('[') + combined.count('{')
    numbers = sum(1 for c in combined if c.isdigit())
    punctuation = combined.count('.') + combined.count(',') + combined.count(':') + combined.count(';')
    
    # Complex indicators
    complex_keywords = ['deluxe', 'remastered', 'anniversary', 'collection', 'anthology', 
                       'complete', 'ultimate', 'definitive', 'box set', 'limited edition',
                       'special edition', 'expanded', 'bonus', 'unreleased', 'rare']
    
    has_complex_keywords = any(keyword in combined for keyword in complex_keywords)
    
    # Scoring system
    complexity_score = 0
    complexity_score += special_chars * 2
    complexity_score += max(0, word_count - 3) * 3
    complexity_score += parentheses * 4
    complexity_score += numbers * 1
    complexity_score += punctuation * 2
    complexity_score += 10 if has_complex_keywords else 0
    
    if complexity_score >= 15:
        return 'complex'
    elif complexity_score >= 6:
        return 'medium'
    else:
        return 'simple'

def create_album_variants(artist_name: str, genres: List[str]) -> List[Dict[str, Any]]:
    """Create album variants with different complexity levels"""
    
    # Simple album titles
    simple_titles = [
        "Greatest Hits",
        "Best Songs", 
        "Live",
        "Essential",
        f"{artist_name}",
        "Collection",
        "Hits",
        "Gold"
    ]
    
    # Medium complexity titles  
    medium_titles = [
        f"The Best of {artist_name}",
        f"{artist_name} Live in Concert",
        f"Essential {artist_name}",
        f"{artist_name} Gold Collection",
        f"The Very Best of {artist_name}",
        f"{artist_name}: Greatest Hits Volume 1",
        f"{artist_name} - Live at Madison Square Garden",
        f"The {artist_name} Collection"
    ]
    
    # Complex titles
    complex_titles = [
        f"{artist_name}: Complete Studio Collection (Deluxe Edition)",
        f"The Definitive {artist_name} Anthology Box Set (Remastered)",
        f"{artist_name} vs. The Orchestra: Symphonic Reimagining",
        f"{artist_name}: Lost Recordings & B-Sides (25th Anniversary Edition)",
        f"The Ultimate {artist_name} Experience: Rare & Unreleased (1965-2024)",
        f"{artist_name}: Live at Woodstock '69 - Complete Performance (Restored)",
        f"Special Limited Edition: {artist_name} Platinum Collection (3-CD Set)",
        f"{artist_name}: The Collector's Edition Box Set - Deluxe Remaster",
        f"Exclusive: {artist_name} Acoustic Sessions (Previously Unreleased)",
        f"{artist_name}: 50th Anniversary Complete Discography (Remastered + Extras)"
    ]
    
    albums = []
    
    # Generate albums for each complexity level
    for title in random.sample(simple_titles, min(2, len(simple_titles))):
        albums.append(create_album_entry(artist_name, title, genres, 'simple'))
    
    for title in random.sample(medium_titles, min(3, len(medium_titles))):
        albums.append(create_album_entry(artist_name, title, genres, 'medium'))
        
    for title in random.sample(complex_titles, min(2, len(complex_titles))):
        albums.append(create_album_entry(artist_name, title, genres, 'complex'))
    
    return albums

def create_album_entry(artist_name: str, album_title: str, genres: List[str], expected_complexity: str) -> Dict[str, Any]:
    """Create a standardized album entry"""
    album_id = f"local-mb-{hashlib.md5(f'{artist_name}-{album_title}'.encode('utf-8')).hexdigest()[:8]}"
    data_hash = hashlib.md5(f"{artist_name.lower()}-{album_title.lower()}".encode('utf-8')).hexdigest()
    
    # Verify complexity matches expectation
    actual_complexity = determine_query_complexity(artist_name, album_title)
    
    return {
        'id': album_id,
        'artist': artist_name,
        'title': album_title,
        'type': 'album',
        'year': random.randint(1960, 2024),
        'genres': genres[:3] if genres else ['rock'],
        'track_count': random.randint(8, 20),
        'duration': random.randint(30, 90),
        'source': 'musicbrainz_local',
        'relevance_score': random.uniform(0.6, 1.0),
        'complexity': actual_complexity,  # Use actual calculated complexity
        'expected_complexity': expected_complexity,  # For validation
        'data_hash': data_hash,
        'collected_at': datetime.now(UTC).isoformat()
    }

def collect_diverse_training_data(base_url="http://192.168.2.13:5001", target_albums=200):
    """Collect diverse training data from local MusicBrainz"""
    
    # Diverse artist searches
    artist_searches = [
        'radiohead', 'beatles', 'pink floyd', 'led zeppelin', 'queen', 'rolling stones',
        'david bowie', 'the who', 'nirvana', 'pearl jam', 'metallica', 'black sabbath',
        'deep purple', 'ac/dc', 'guns n roses', 'red hot chili peppers', 'foo fighters',
        'green day', 'linkin park', 'coldplay', 'u2', 'oasis', 'blur', 'the cure',
        'depeche mode', 'new order', 'joy division', 'the smiths', 'radiohead',
        'muse', 'arcade fire', 'the strokes', 'interpol', 'the killers'
    ]
    
    all_albums = []
    
    for search_term in artist_searches:
        if len(all_albums) >= target_albums:
            break
            
        try:
            print(f"Searching for: {search_term}")
            
            response = requests.get(f"{base_url}/search?type=all&query={search_term}", timeout=10)
            
            if response.status_code != 200:
                print(f"  Error: HTTP {response.status_code}")
                continue
                
            data = response.json()
            
            if not data:
                print(f"  No results found")
                continue
                
            print(f"  Found {len(data)} results")
            
            # Process results to get unique artists
            unique_artists = {}
            
            for item in data:
                artist_data = item.get('artist')
                if not artist_data or not isinstance(artist_data, dict):
                    continue
                    
                artist_name = artist_data.get('artistname', '').strip()
                genres = artist_data.get('genres', ['rock'])
                artist_id = artist_data.get('id', '')
                
                if artist_name and artist_id not in unique_artists:
                    unique_artists[artist_id] = {
                        'name': artist_name,
                        'genres': genres
                    }
            
            # Generate albums for each unique artist
            for artist_info in list(unique_artists.values())[:3]:  # Max 3 artists per search
                albums = create_album_variants(artist_info['name'], artist_info['genres'])
                all_albums.extend(albums)
                
                print(f"  + Generated {len(albums)} albums for {artist_info['name']}")
                
        except Exception as e:
            print(f"  Error processing {search_term}: {e}")
            continue
    
    return all_albums

if __name__ == "__main__":
    print("Generating comprehensive training dataset from local MusicBrainz...")
    
    albums = collect_diverse_training_data(target_albums=500)
    
    # Analyze complexity distribution
    complexity_counts = {'simple': 0, 'medium': 0, 'complex': 0}
    for album in albums:
        complexity_counts[album['complexity']] += 1
    
    print(f"\nSUMMARY:")
    print(f"Total albums: {len(albums)}")
    print(f"Complexity distribution:")
    for complexity, count in complexity_counts.items():
        percentage = (count / len(albums)) * 100 if albums else 0
        print(f"  {complexity.capitalize()}: {count} ({percentage:.1f}%)")
    
    # Save comprehensive dataset
    output_file = 'comprehensive_local_mb_training.json'
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(albums, f, indent=2, ensure_ascii=False)
    
    print(f"\nSaved {len(albums)} albums to {output_file}")
    print(f"Ready for ML training!")
    
    # Show sample data
    print(f"\nSample albums:")
    for complexity in ['simple', 'medium', 'complex']:
        examples = [a for a in albums if a['complexity'] == complexity][:2]
        for example in examples:
            print(f"  {complexity.upper()}: {example['artist']} - {example['title']}")