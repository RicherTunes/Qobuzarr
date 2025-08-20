#!/usr/bin/env python3
"""
Lidarr Library Extractor for Complex Example Repository Building

This tool extracts all albums from your Lidarr library and analyzes them for complexity
to build a comprehensive test repository for the Unicode query builder.

Usage:
    python scripts/extract_lidarr_library.py
    python scripts/extract_lidarr_library.py --complexity-threshold 0.5 --limit 1000
    python scripts/extract_lidarr_library.py --export-format json --output library_analysis.json
"""

import asyncio
import aiohttp
import argparse
import json
import sqlite3
import unicodedata
from dataclasses import dataclass, asdict
from typing import List, Dict, Optional, Set
import re
from pathlib import Path
import logging
from datetime import datetime

# Setup logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

@dataclass
class LibraryAlbum:
    """Album extracted from Lidarr library with complexity analysis"""
    lidarr_id: int
    artist: str
    album: str
    year: Optional[int]
    genre: str
    status: str
    complexity_score: float
    complexity_factors: List[str]
    unicode_scripts: List[str]
    special_char_count: int
    total_length: int
    has_compilation_indicators: bool
    has_edition_indicators: bool
    has_featured_artists: bool
    parenthetical_count: int

class ComplexityAnalyzer:
    """Analyzes album/artist complexity to identify challenging search cases"""
    
    EDITION_TERMS = [
        'deluxe', 'remaster', 'anniversary', 'special', 'expanded', 'collector',
        'limited', 'bonus', 'extended', 'ultimate', 'complete', 'platinum',
        'gold', 'diamond', 'super', 'mega', 'box set', 'collection'
    ]
    
    COMPILATION_INDICATORS = [
        'various artists', 'various', 'compilation', 'v.a.', 'va', 'comp.',
        'mixed by', 'compiled by', 'selected by', 'curated by', 'best of',
        'greatest hits', 'anthology', 'treasury', 'soundtracks'
    ]
    
    FEATURED_PATTERNS = [
        r'\b(feat|ft|featuring)\.?\s+',
        r'\s+&\s+',
        r'\s+with\s+',
        r'\s+vs\.?\s+',
        r'\s+x\s+',
        r'\s+meets\s+'
    ]
    
    def calculate_complexity_score(self, artist: str, album: str) -> tuple[float, List[str]]:
        """Calculate complexity score and identify contributing factors"""
        score = 0.0
        factors = []
        
        full_text = f"{artist} {album}"
        
        # 1. Unicode complexity (0-0.4 points)
        unicode_chars = sum(1 for c in full_text if ord(c) > 127)
        if unicode_chars > 0:
            unicode_score = min(unicode_chars / len(full_text) * 2, 0.4)
            score += unicode_score
            factors.append(f"unicode_chars_{unicode_chars}")
            
        # 2. Multiple Unicode scripts (0-0.3 points)  
        scripts = self.detect_unicode_scripts(full_text)
        if len(scripts) > 1:
            score += 0.3
            factors.append(f"multiple_scripts_{'+'.join(scripts)}")
            
        # 3. Special character density (0-0.2 points)
        special_chars = sum(1 for c in full_text if not c.isalnum() and not c.isspace())
        if special_chars > 5:
            special_score = min(special_chars / len(full_text), 0.2)
            score += special_score
            factors.append(f"special_chars_{special_chars}")
            
        # 4. Compilation indicators (0-0.4 points)
        if any(indicator in artist.lower() for indicator in self.COMPILATION_INDICATORS):
            score += 0.4
            factors.append("compilation_album")
            
        # 5. Edition indicators (0-0.2 points)
        edition_count = sum(1 for term in self.EDITION_TERMS if term in album.lower())
        if edition_count > 0:
            edition_score = min(edition_count * 0.1, 0.2)
            score += edition_score
            factors.append(f"edition_terms_{edition_count}")
            
        # 6. Featured artists (0-0.2 points)
        featured_matches = sum(1 for pattern in self.FEATURED_PATTERNS 
                              if re.search(pattern, full_text, re.IGNORECASE))
        if featured_matches > 0:
            score += min(featured_matches * 0.1, 0.2)
            factors.append(f"featured_artists_{featured_matches}")
            
        # 7. Nested punctuation (0-0.3 points)
        parentheticals = full_text.count('(') + full_text.count('[')
        if parentheticals > 2:
            score += min(parentheticals * 0.1, 0.3)
            factors.append(f"nested_punctuation_{parentheticals}")
            
        # 8. Length complexity (0-0.2 points)
        if len(full_text) > 80:
            length_score = min((len(full_text) - 80) / 200, 0.2)
            score += length_score
            factors.append(f"long_names_{len(full_text)}")
            
        # 9. Number density (years, volumes, etc.) (0-0.1 points)
        numbers = len(re.findall(r'\b\d+\b', full_text))
        if numbers > 2:
            score += min(numbers * 0.02, 0.1)
            factors.append(f"number_heavy_{numbers}")
            
        return min(score, 1.0), factors
    
    def detect_unicode_scripts(self, text: str) -> List[str]:
        """Detect different Unicode scripts in text"""
        scripts = set()
        
        for char in text:
            if ord(char) > 127:
                script_name = unicodedata.name(char, 'UNKNOWN').split()[0]
                if script_name != 'UNKNOWN':
                    scripts.add(script_name)
        
        # Simplify script names
        script_mapping = {
            'LATIN': 'Latin',
            'CYRILLIC': 'Cyrillic', 
            'GREEK': 'Greek',
            'CJK': 'CJK',
            'ARABIC': 'Arabic',
            'HEBREW': 'Hebrew'
        }
        
        simplified_scripts = []
        for script in scripts:
            for key, value in script_mapping.items():
                if key in script:
                    simplified_scripts.append(value)
                    break
        
        return list(set(simplified_scripts))

class LidarrLibraryExtractor:
    """Extracts album data from Lidarr API for complexity analysis"""
    
    def __init__(self, lidarr_url: str, api_key: str):
        self.lidarr_url = lidarr_url.rstrip('/')
        self.api_key = api_key
        self.complexity_analyzer = ComplexityAnalyzer()
        
    async def extract_complete_library(self, complexity_threshold: float = 0.3, 
                                     limit: Optional[int] = None) -> List[LibraryAlbum]:
        """Extract all albums from Lidarr and analyze complexity"""
        
        logger.info(f"Extracting library from {self.lidarr_url}")
        logger.info(f" Complexity threshold: {complexity_threshold}")
        logger.info(f" Limit: {limit or 'No limit'}")
        
        albums = []
        page = 1
        page_size = 100
        
        async with aiohttp.ClientSession() as session:
            while True:
                # Get albums page by page to handle large libraries
                url = f"{self.lidarr_url}/api/v1/album"
                params = {
                    'apikey': self.api_key,
                    'page': page,
                    'pageSize': page_size,
                    'sortKey': 'albumType',
                    'sortDirection': 'ascending'
                }
                
                try:
                    logger.info(f"📖 Fetching page {page}...")
                    async with session.get(url, params=params) as response:
                        if response.status != 200:
                            logger.error(f"❌ Lidarr API error: {response.status}")
                            break
                            
                        page_data = await response.json()
                        
                        if not page_data:
                            logger.info("📄 No more albums found")
                            break
                            
                        # Process this page
                        for album_data in page_data:
                            try:
                                album = self.process_album_data(album_data)
                                if album and album.complexity_score >= complexity_threshold:
                                    albums.append(album)
                                    
                                    if limit and len(albums) >= limit:
                                        logger.info(f"🎯 Reached limit of {limit} albums")
                                        return albums
                                        
                            except Exception as e:
                                logger.warning(f"⚠️ Error processing album: {e}")
                                continue
                        
                        logger.info(f"✅ Page {page}: {len(page_data)} albums, {len(albums)} complex so far")
                        
                        # Check if we got fewer results than page size (last page)
                        if len(page_data) < page_size:
                            logger.info("📄 Reached end of library")
                            break
                            
                        page += 1
                        
                        # Rate limiting - be nice to Lidarr
                        await asyncio.sleep(0.1)
                        
                except Exception as e:
                    logger.error(f"❌ Error fetching page {page}: {e}")
                    break
        
        logger.info(f"🏁 Extraction complete: {len(albums)} complex albums found")
        return albums
    
    def process_album_data(self, album_data: dict) -> Optional[LibraryAlbum]:
        """Process raw Lidarr album data into LibraryAlbum with complexity analysis"""
        
        try:
            # Extract basic info
            artist_name = album_data.get('artist', {}).get('artistName', 'Unknown Artist')
            album_title = album_data.get('title', 'Unknown Album')
            year = album_data.get('releaseDate', '')
            genre = ', '.join(album_data.get('genres', []))
            status = album_data.get('monitored', False)
            
            # Parse year from release date
            release_year = None
            if year:
                try:
                    release_year = int(year[:4]) if len(year) >= 4 else None
                except:
                    pass
            
            # Calculate complexity
            complexity_score, factors = self.complexity_analyzer.calculate_complexity_score(
                artist_name, album_title)
            
            # Additional analysis
            full_text = f"{artist_name} {album_title}"
            unicode_scripts = self.complexity_analyzer.detect_unicode_scripts(full_text)
            special_chars = sum(1 for c in full_text if not c.isalnum() and not c.isspace())
            
            # Pattern detection
            has_compilation = any(indicator in artist_name.lower() 
                                for indicator in self.complexity_analyzer.COMPILATION_INDICATORS)
            has_edition = any(term in album_title.lower() 
                            for term in self.complexity_analyzer.EDITION_TERMS)
            has_featured = any(re.search(pattern, full_text, re.IGNORECASE) 
                             for pattern in self.complexity_analyzer.FEATURED_PATTERNS)
            parenthetical_count = full_text.count('(') + full_text.count('[')
            
            return LibraryAlbum(
                lidarr_id=album_data.get('id', 0),
                artist=artist_name,
                album=album_title,
                year=release_year,
                genre=genre,
                status='monitored' if status else 'unmonitored',
                complexity_score=complexity_score,
                complexity_factors=factors,
                unicode_scripts=unicode_scripts,
                special_char_count=special_chars,
                total_length=len(full_text),
                has_compilation_indicators=has_compilation,
                has_edition_indicators=has_edition,
                has_featured_artists=has_featured,
                parenthetical_count=parenthetical_count
            )
            
        except Exception as e:
            logger.warning(f"⚠️ Error processing album data: {e}")
            return None

    def save_to_database(self, albums: List[LibraryAlbum], db_path: str = "scripts/lidarr_library_analysis.db"):
        """Save extracted library to SQLite database for analysis"""
        
        logger.info(f"💾 Saving {len(albums)} albums to {db_path}")
        
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        
        # Create table
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS library_albums (
                lidarr_id INTEGER PRIMARY KEY,
                artist TEXT NOT NULL,
                album TEXT NOT NULL,
                year INTEGER,
                genre TEXT,
                status TEXT,
                complexity_score REAL,
                complexity_factors TEXT,
                unicode_scripts TEXT,
                special_char_count INTEGER,
                total_length INTEGER,
                has_compilation_indicators BOOLEAN,
                has_edition_indicators BOOLEAN,
                has_featured_artists BOOLEAN,
                parenthetical_count INTEGER,
                extracted_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
        ''')
        
        # Insert albums
        for album in albums:
            cursor.execute('''
                INSERT OR REPLACE INTO library_albums 
                (lidarr_id, artist, album, year, genre, status, complexity_score,
                 complexity_factors, unicode_scripts, special_char_count, total_length,
                 has_compilation_indicators, has_edition_indicators, has_featured_artists,
                 parenthetical_count)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                album.lidarr_id, album.artist, album.album, album.year, album.genre,
                album.status, album.complexity_score, json.dumps(album.complexity_factors),
                json.dumps(album.unicode_scripts), album.special_char_count, album.total_length,
                album.has_compilation_indicators, album.has_edition_indicators,
                album.has_featured_artists, album.parenthetical_count
            ))
        
        conn.commit()
        conn.close()
        
        logger.info(f"✅ Saved to database: {db_path}")

    def generate_summary_report(self, albums: List[LibraryAlbum]) -> Dict:
        """Generate analysis summary of extracted library"""
        
        if not albums:
            return {"error": "No albums to analyze"}
        
        # Calculate statistics
        total_albums = len(albums)
        unicode_albums = len([a for a in albums if any(ord(c) > 127 for c in f"{a.artist} {a.album}")])
        compilation_albums = len([a for a in albums if a.has_compilation_indicators])
        edition_albums = len([a for a in albums if a.has_edition_indicators])
        featured_albums = len([a for a in albums if a.has_featured_artists])
        
        # Complexity distribution
        complexity_ranges = {
            'low': len([a for a in albums if a.complexity_score < 0.3]),
            'medium': len([a for a in albums if 0.3 <= a.complexity_score < 0.7]),
            'high': len([a for a in albums if a.complexity_score >= 0.7])
        }
        
        # Most complex albums
        most_complex = sorted(albums, key=lambda a: a.complexity_score, reverse=True)[:10]
        
        # Unicode scripts found
        all_scripts = set()
        for album in albums:
            all_scripts.update(album.unicode_scripts)
        
        # Genre distribution for complex albums
        genre_complexity = {}
        for album in albums:
            if album.complexity_score > 0.5:
                genre = album.genre.split(',')[0].strip() if album.genre else 'Unknown'
                genre_complexity[genre] = genre_complexity.get(genre, 0) + 1
        
        return {
            'extraction_date': datetime.now().isoformat(),
            'total_albums': total_albums,
            'unicode_albums': unicode_albums,
            'unicode_percentage': (unicode_albums / total_albums * 100) if total_albums > 0 else 0,
            'compilation_albums': compilation_albums,
            'edition_albums': edition_albums,
            'featured_albums': featured_albums,
            'complexity_distribution': complexity_ranges,
            'unicode_scripts_found': list(all_scripts),
            'most_complex_albums': [
                {
                    'artist': album.artist,
                    'album': album.album,
                    'complexity_score': album.complexity_score,
                    'factors': album.complexity_factors
                }
                for album in most_complex
            ],
            'genre_complexity_leaders': dict(sorted(genre_complexity.items(), 
                                                   key=lambda x: x[1], reverse=True)[:10])
        }

async def main():
    """Main extraction workflow"""
    parser = argparse.ArgumentParser(description="Extract Lidarr library for complex example building")
    parser.add_argument('--lidarr-url', default='http://192.168.2.50:8686', 
                       help='Lidarr instance URL')
    parser.add_argument('--api-key', default='ca6a612bb8f84d9c976fcac967331da5',
                       help='Lidarr API key')
    parser.add_argument('--complexity-threshold', type=float, default=0.3,
                       help='Minimum complexity score to include (0.0-1.0)')
    parser.add_argument('--limit', type=int, default=None,
                       help='Maximum number of complex albums to extract')
    parser.add_argument('--export-format', choices=['json', 'csv', 'database'], default='database',
                       help='Export format for results')
    parser.add_argument('--output', default='scripts/lidarr_library_analysis',
                       help='Output file path (without extension)')
    
    args = parser.parse_args()
    
    print("Lidarr Library Complexity Extraction")
    print("=" * 50)
    print(f" Lidarr URL: {args.lidarr_url}")
    print(f" API Key: {args.api_key[:8]}...")
    print(f" Complexity threshold: {args.complexity_threshold}")
    print(f" Limit: {args.limit or 'No limit'}")
    print("=" * 50)
    
    extractor = LidarrLibraryExtractor(args.lidarr_url, args.api_key)
    
    try:
        # Extract library
        albums = await extractor.extract_complete_library(
            complexity_threshold=args.complexity_threshold,
            limit=args.limit
        )
        
        if not albums:
            print("❌ No complex albums found. Try lowering --complexity-threshold")
            return
        
        # Generate summary
        summary = extractor.generate_summary_report(albums)
        
        # Export results
        if args.export_format == 'database':
            extractor.save_to_database(albums, f"{args.output}.db")
        elif args.export_format == 'json':
            with open(f"{args.output}.json", 'w', encoding='utf-8') as f:
                json.dump({
                    'summary': summary,
                    'albums': [asdict(album) for album in albums]
                }, f, indent=2, ensure_ascii=False)
        elif args.export_format == 'csv':
            import pandas as pd
            df = pd.DataFrame([asdict(album) for album in albums])
            df.to_csv(f"{args.output}.csv", index=False, encoding='utf-8')
        
        # Print summary
        print("\n EXTRACTION SUMMARY")
        print("=" * 50)
        print(f"Total complex albums: {summary['total_albums']}")
        print(f"Unicode albums: {summary['unicode_albums']} ({summary['unicode_percentage']:.1f}%)")
        print(f"Compilation albums: {summary['compilation_albums']}")
        print(f"Edition albums: {summary['edition_albums']}")
        print(f"Featured artist albums: {summary['featured_albums']}")
        print(f"Unicode scripts found: {', '.join(summary['unicode_scripts_found'])}")
        
        print(f"\n🏆 TOP 5 MOST COMPLEX ALBUMS:")
        for i, album in enumerate(summary['most_complex_albums'][:5], 1):
            print(f"{i}. {album['artist']} - {album['album']}")
            print(f"   Score: {album['complexity_score']:.3f} | Factors: {', '.join(album['factors'])}")
        
        print(f"\n COMPLEXITY DISTRIBUTION:")
        dist = summary['complexity_distribution']
        print(f"Low (0.0-0.3): {dist['low']} albums")
        print(f"Medium (0.3-0.7): {dist['medium']} albums") 
        print(f"High (0.7-1.0): {dist['high']} albums")
        
        print(f"\n🎵 COMPLEX GENRES:")
        for genre, count in list(summary['genre_complexity_leaders'].items())[:5]:
            print(f"{genre}: {count} complex albums")
        
        print(f"\n💾 Results saved to: {args.output}.{args.export_format}")
        print("\n🎯 Next steps:")
        print("1. Run: python scripts/analyze_unicode_gaps.py")
        print("2. Validate gaps with: python scripts/validate_qobuz_gaps.py")
        print("3. Generate test cases with: python scripts/generate_complex_test_cases.py")
        
    except Exception as e:
        logger.error(f"💥 Extraction failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())