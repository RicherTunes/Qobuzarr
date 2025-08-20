#!/usr/bin/env python3
"""
Simple Unicode Gap Analysis (No Emoji Issues)

Analyzes your extracted library for Unicode system gaps.
"""

import sqlite3
import json
import unicodedata
import re
import argparse
from datetime import datetime

def analyze_unicode_gaps(database_path, min_complexity=0.4, test_sample=None):
    """Analyze library for Unicode system gaps"""
    
    print(f"Loading library from: {database_path}")
    
    # Load library data
    conn = sqlite3.connect(database_path)
    cursor = conn.cursor()
    
    query = '''
        SELECT * FROM library_albums 
        WHERE complexity_score >= ? 
        ORDER BY complexity_score DESC
    '''
    
    if test_sample:
        query += f' LIMIT {test_sample}'
    
    cursor.execute(query, (min_complexity,))
    columns = [description[0] for description in cursor.description]
    
    albums = []
    for row in cursor.fetchall():
        album_dict = dict(zip(columns, row))
        albums.append(album_dict)
    
    conn.close()
    
    print(f"Analyzing {len(albums)} complex albums...")
    
    # Analyze for gaps
    gaps = []
    
    for album in albums:
        artist = album['artist']
        album_title = album['album']
        
        # Simulate Unicode query generation
        variants = simulate_unicode_variants(artist, album_title)
        
        # Predict if this might fail
        gap_risk = predict_gap_risk(artist, album_title, album['complexity_score'])
        
        if gap_risk['is_gap']:
            gaps.append({
                'artist': artist,
                'album': album_title,
                'complexity_score': album['complexity_score'],
                'variants_generated': variants,
                'predicted_issues': gap_risk['issues'],
                'gap_severity': gap_risk['severity']
            })
    
    # Generate report
    report = {
        'analysis_date': datetime.now().isoformat(),
        'total_analyzed': len(albums),
        'predicted_gaps': len(gaps),
        'gap_rate': len(gaps) / len(albums) if albums else 0,
        'gaps': gaps
    }
    
    # Save results
    with open('unicode_gaps_analysis.json', 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    
    # Display results
    print()
    print("GAP ANALYSIS RESULTS")
    print("=" * 40)
    print(f"Total analyzed: {len(albums)}")
    print(f"Predicted gaps: {len(gaps)}")
    print(f"Gap rate: {len(gaps)/len(albums)*100:.1f}%")
    
    print()
    print("TOP PREDICTED GAPS:")
    for i, gap in enumerate(gaps[:5], 1):
        print(f"{i}. {gap['artist']} - {gap['album'][:60]}...")
        print(f"   Score: {gap['complexity_score']:.3f}")
        print(f"   Issues: {', '.join(gap['predicted_issues'])}")
        print()
    
    print(f"Results saved to: unicode_gaps_analysis.json")
    print()
    print("Next steps:")
    print("1. python scripts/validate_qobuz_gaps.py")
    print("2. python scripts/generate_complex_test_cases.py")
    
    return gaps

def simulate_unicode_variants(artist, album):
    """Simulate our Unicode query builder"""
    variants = []
    full_query = f"{artist} {album}"
    
    # Original
    variants.append(full_query)
    
    # ASCII folding
    ascii_version = fold_to_ascii(full_query)
    if ascii_version != full_query:
        variants.append(ascii_version)
    
    # Component searches
    variants.append(fold_to_ascii(artist))
    variants.append(fold_to_ascii(album))
    
    return list(dict.fromkeys(variants))

def fold_to_ascii(text):
    """Simple ASCII folding"""
    normalized = unicodedata.normalize('NFD', text)
    ascii_text = ''.join(c for c in normalized if unicodedata.category(c) != 'Mn')
    return unicodedata.normalize('NFC', ascii_text)

def predict_gap_risk(artist, album, complexity_score):
    """Predict if an album might have search gaps"""
    
    issues = []
    severity = 'low'
    
    full_text = f"{artist} {album}"
    
    # Check for issues
    if any(ord(c) > 255 for c in full_text):
        issues.append('high_unicode')
        severity = 'medium'
    
    if len(full_text) > 100:
        issues.append('excessive_length')
        severity = 'medium'
    
    if full_text.count('(') + full_text.count('[') > 3:
        issues.append('complex_punctuation')
        severity = 'medium'
    
    if any(term in artist.lower() for term in ['various', 'compilation']):
        issues.append('compilation_complexity')
        severity = 'high'
    
    if complexity_score > 0.7:
        issues.append('extreme_complexity')
        severity = 'high'
    
    # Predict gap if multiple issues or high severity
    is_gap = len(issues) >= 2 or severity == 'high'
    
    return {
        'is_gap': is_gap,
        'issues': issues,
        'severity': severity
    }

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--database', default='lidarr_library_analysis.db')
    parser.add_argument('--min-complexity', type=float, default=0.4)
    parser.add_argument('--test-sample', type=int, default=None)
    
    args = parser.parse_args()
    
    try:
        gaps = analyze_unicode_gaps(args.database, args.min_complexity, args.test_sample)
        print(f"SUCCESS: Found {len(gaps)} potential gaps in your library!")
        
    except FileNotFoundError:
        print("ERROR: Database not found")
        print("First run: python extract_lidarr_library.py")
    except Exception as e:
        print(f"ERROR: {e}")

if __name__ == "__main__":
    main()