#!/usr/bin/env python3
"""
MusicBrainz Data Extraction Script for ML Training

Extracts album and artist data from a local MusicBrainz instance
to create training datasets for Qobuzarr ML models.

Usage:
    python extract_musicbrainz_data.py --mb-url http://192.168.2.13:5001/ --output albums.json
    python extract_musicbrainz_data.py --config config.json
"""

import argparse
import json
import logging
import os
import sys
import time
from datetime import datetime
from typing import List, Dict, Any, Optional
from urllib.parse import urljoin
import requests
import psycopg2
from psycopg2.extras import RealDictCursor
from dataclasses import dataclass, asdict
from tqdm import tqdm

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('musicbrainz_extraction.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

@dataclass
class AlbumData:
    """Album data structure matching Qobuzarr's AlbumData format"""
    lidarr_id: int
    artist_name: str
    artist_id: str
    album_title: str
    album_title_clean: str
    album_type: str
    release_date: str
    release_year: str
    track_count: int
    monitored: bool
    search_query: str
    disambiguation: str
    foreign_album_id: str
    genres: List[str]
    overview: str
    album_id: int
    artist_metadata_id: int

@dataclass
class ExtractorConfig:
    """Configuration for MusicBrainz extraction"""
    mb_url: str
    mb_database_url: Optional[str] = None
    max_albums: int = 100000
    min_track_count: int = 3
    max_track_count: int = 50
    include_genres: List[str] = None
    exclude_album_types: List[str] = None
    rate_limit_delay: float = 0.1
    batch_size: int = 1000
    output_format: str = "json"

class MusicBrainzExtractor:
    """Extracts album data from MusicBrainz instance"""
    
    def __init__(self, config: ExtractorConfig):
        self.config = config
        self.session = requests.Session()
        self.session.headers.update({
            'User-Agent': 'Qobuzarr-ML-Trainer/1.0 (https://github.com/your-repo)',
            'Accept': 'application/json'
        })
        self.db_connection = None
        
    def connect_to_database(self):
        """Connect to MusicBrainz PostgreSQL database for direct access"""
        if not self.config.mb_database_url:
            logger.info("No database URL provided, using API-only extraction")
            return
            
        try:
            self.db_connection = psycopg2.connect(self.config.mb_database_url)
            logger.info("Successfully connected to MusicBrainz database")
        except Exception as e:
            logger.warning(f"Failed to connect to database: {e}. Using API fallback.")
            self.db_connection = None
    
    def extract_albums_from_database(self) -> List[AlbumData]:
        """Extract albums directly from MusicBrainz database (faster) with incremental support"""
        if not self.db_connection:
            return self.extract_albums_from_api()
            
        logger.info("Extracting albums from MusicBrainz database...")
        
        # Check for checkpoint file
        checkpoint_data = self._load_checkpoint()
        start_offset = checkpoint_data.get('last_offset', 0) if checkpoint_data else 0
        
        query = """
        SELECT DISTINCT
            r.gid as release_id,
            r.name as album_title,
            r.comment as disambiguation,
            rg.name as release_group_name,
            rgt.name as album_type,
            a.name as artist_name,
            a.gid as artist_id,
            DATE_PART('year', r.date_year) as release_year,
            r.date_year || '-' || COALESCE(r.date_month, 1) || '-' || COALESCE(r.date_day, 1) as release_date,
            (SELECT COUNT(*) FROM medium m 
             JOIN track t ON m.id = t.medium 
             WHERE m.release = r.id) as track_count
        FROM release r
        JOIN release_group rg ON r.release_group = rg.id
        JOIN release_group_primary_type rgt ON rg.type = rgt.id
        JOIN artist_credit ac ON r.artist_credit = ac.id
        JOIN artist_credit_name acn ON ac.id = acn.artist_credit
        JOIN artist a ON acn.artist = a.id
        WHERE r.status = 1  -- Official releases only
          AND rgt.name IN ('Album', 'EP', 'Single')
          AND r.date_year IS NOT NULL
          AND r.date_year >= 1950
        ORDER BY r.date_year DESC, a.name, r.name
        OFFSET %s LIMIT %s
        """
        
        albums = []
        
        # If resuming, load existing albums
        if checkpoint_data and 'albums' in checkpoint_data:
            albums = [AlbumData(**album) for album in checkpoint_data['albums']]
            logger.info(f"Resuming from checkpoint: {len(albums)} albums already processed")
        
        try:
            # Incremental extraction
            current_offset = start_offset
            batch_size = getattr(self.config, 'incremental_size', 1000)
            
            while len(albums) < self.config.max_albums:
                remaining = self.config.max_albums - len(albums)
                current_batch_size = min(batch_size, remaining)
                
                logger.info(f"Extracting batch: offset={current_offset}, size={current_batch_size}")
                
                with self.db_connection.cursor(cursor_factory=RealDictCursor) as cursor:
                    cursor.execute(query, (current_offset, current_batch_size))
                    rows = cursor.fetchall()
                    
                    if not rows:
                        logger.info("No more data available")
                        break
                    
                    batch_albums = []
                    for i, row in enumerate(tqdm(rows, desc=f"Processing batch {current_offset//batch_size + 1}")):
                        if row['track_count'] < self.config.min_track_count or row['track_count'] > self.config.max_track_count:
                            continue
                            
                        album = AlbumData(
                            lidarr_id=len(albums) + len(batch_albums) + 1,
                            artist_name=row['artist_name'] or "Unknown Artist",
                            artist_id=row['artist_id'] or "",
                            album_title=row['album_title'] or "Unknown Album",
                            album_title_clean=self._clean_album_title(row['album_title'] or ""),
                            album_type=row['album_type'] or "Album",
                            release_date=str(row['release_date']) if row['release_date'] else "",
                            release_year=str(int(row['release_year'])) if row['release_year'] else "",
                            track_count=int(row['track_count']) if row['track_count'] else 0,
                            monitored=True,
                            search_query=f"{row['artist_name']} {row['album_title']}",
                            disambiguation=row['disambiguation'] or "",
                            foreign_album_id=row['release_id'] or "",
                            genres=self._extract_genres(row['release_id']) if row['release_id'] else [],
                            overview="",
                            album_id=len(albums) + len(batch_albums) + 1,
                            artist_metadata_id=hash(row['artist_id'] or "") % 1000000
                        )
                        batch_albums.append(album)
                    
                    albums.extend(batch_albums)
                    current_offset += len(rows)
                    
                    # Save checkpoint
                    self._save_checkpoint({
                        'last_offset': current_offset,
                        'albums': [asdict(album) for album in albums],
                        'timestamp': datetime.now().isoformat()
                    })
                    
                    logger.info(f"Progress: {len(albums)}/{self.config.max_albums} albums extracted")
                    
                    # Break if we got fewer rows than expected (end of data)
                    if len(rows) < current_batch_size:
                        break
                        
        except Exception as e:
            logger.error(f"Database extraction failed: {e}")
            return self.extract_albums_from_api()
            
        # Clean up checkpoint file when done
        self._cleanup_checkpoint()
        
        logger.info(f"Extracted {len(albums)} albums from database")
        return albums
    
    def extract_albums_from_api(self) -> List[AlbumData]:
        """Extract albums using MusicBrainz API (slower but more compatible)"""
        logger.info("Extracting albums from MusicBrainz API...")
        
        albums = []
        offset = 0
        
        with tqdm(total=self.config.max_albums, desc="API extraction") as pbar:
            while len(albums) < self.config.max_albums:
                try:
                    # Search for releases
                    url = urljoin(self.config.mb_url, 'ws/2/release')
                    params = {
                        'query': 'status:official AND primarytype:(Album OR EP)',
                        'limit': min(100, self.config.max_albums - len(albums)),
                        'offset': offset,
                        'fmt': 'json',
                        'inc': 'artist-credits+release-groups+recordings'
                    }
                    
                    response = self.session.get(url, params=params)
                    if response.status_code != 200:
                        logger.error(f"API request failed: {response.status_code}")
                        break
                        
                    data = response.json()
                    releases = data.get('releases', [])
                    
                    if not releases:
                        logger.info("No more releases found")
                        break
                        
                    for release in releases:
                        album = self._parse_release_data(release, len(albums) + 1)
                        if album and self._is_valid_album(album):
                            albums.append(album)
                            pbar.update(1)
                            
                    offset += len(releases)
                    time.sleep(self.config.rate_limit_delay)  # Rate limiting
                    
                except Exception as e:
                    logger.error(f"Error processing batch at offset {offset}: {e}")
                    offset += 100
                    continue
                    
        logger.info(f"Extracted {len(albums)} albums from API")
        return albums
    
    def _parse_release_data(self, release: Dict[str, Any], album_id: int) -> Optional[AlbumData]:
        """Parse MusicBrainz release data into AlbumData format"""
        try:
            # Extract artist information
            artist_credits = release.get('artist-credit', [])
            if not artist_credits:
                return None
                
            primary_artist = artist_credits[0]
            artist_name = primary_artist.get('name', 'Unknown Artist')
            artist_id = primary_artist.get('artist', {}).get('id', '')
            
            # Extract album information  
            album_title = release.get('title', 'Unknown Album')
            release_group = release.get('release-group', {})
            album_type = release_group.get('primary-type', 'Album')
            
            # Extract release date
            release_date = release.get('date', '')
            release_year = release_date.split('-')[0] if release_date else ''
            
            # Count tracks
            media = release.get('media', [])
            track_count = sum(len(medium.get('tracks', [])) for medium in media)
            
            return AlbumData(
                lidarr_id=album_id,
                artist_name=artist_name,
                artist_id=artist_id,
                album_title=album_title,
                album_title_clean=self._clean_album_title(album_title),
                album_type=album_type,
                release_date=release_date,
                release_year=release_year,
                track_count=track_count,
                monitored=True,
                search_query=f"{artist_name} {album_title}",
                disambiguation=release.get('disambiguation', ''),
                foreign_album_id=release.get('id', ''),
                genres=self._extract_genres_from_tags(release.get('tags', [])),
                overview='',
                album_id=album_id,
                artist_metadata_id=hash(artist_id) % 1000000
            )
            
        except Exception as e:
            logger.warning(f"Failed to parse release: {e}")
            return None
    
    def _clean_album_title(self, title: str) -> str:
        """Clean album title for better matching"""
        # Remove common suffixes and prefixes
        cleaners = [
            ' (Remastered)', ' (Deluxe Edition)', ' (Special Edition)',
            ' (Expanded Edition)', ' (Anniversary Edition)', ' (Remaster)',
            '[Remastered]', '[Deluxe Edition]', '[Special Edition]'
        ]
        
        cleaned = title
        for cleaner in cleaners:
            cleaned = cleaned.replace(cleaner, '')
            
        return cleaned.strip()
    
    def _extract_genres(self, release_id: str) -> List[str]:
        """Extract genres from database for a release"""
        if not self.db_connection:
            return []
            
        try:
            query = """
            SELECT DISTINCT t.name
            FROM tag t
            JOIN release_tag rt ON t.id = rt.tag
            WHERE rt.release = (SELECT id FROM release WHERE gid = %s)
            ORDER BY t.name
            LIMIT 5
            """
            
            with self.db_connection.cursor() as cursor:
                cursor.execute(query, (release_id,))
                return [row[0] for row in cursor.fetchall()]
                
        except Exception:
            return []
    
    def _extract_genres_from_tags(self, tags: List[Dict[str, Any]]) -> List[str]:
        """Extract genres from API tag data"""
        return [tag.get('name', '') for tag in tags[:5] if tag.get('name')]
    
    def _is_valid_album(self, album: AlbumData) -> bool:
        """Validate album data for training suitability"""
        if album.track_count < self.config.min_track_count:
            return False
        if album.track_count > self.config.max_track_count:
            return False
        if not album.artist_name or album.artist_name == 'Unknown Artist':
            return False
        if not album.album_title or album.album_title == 'Unknown Album':
            return False
        if self.config.exclude_album_types and album.album_type in self.config.exclude_album_types:
            return False
            
        return True
    
    def save_dataset(self, albums: List[AlbumData], output_path: str):
        """Save extracted data in Qobuzarr-compatible format"""
        dataset = {
            "version": "1.0.0",
            "created_at": datetime.now().isoformat(),
            "source": f"MusicBrainz ({self.config.mb_url})",
            "total_albums": len(albums),
            "albums": [asdict(album) for album in albums]
        }
        
        logger.info(f"Saving {len(albums)} albums to {output_path}")
        
        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(dataset, f, indent=2, ensure_ascii=False)
            
        # Also save a CSV for analysis
        csv_path = output_path.replace('.json', '.csv')
        self._save_csv(albums, csv_path)
        
        logger.info(f"Dataset saved to {output_path} and {csv_path}")
    
    def _load_checkpoint(self) -> Optional[Dict[str, Any]]:
        """Load checkpoint data for resuming extraction"""
        checkpoint_file = getattr(self.config, 'checkpoint_file', 'extraction_checkpoint.json')
        
        if not os.path.exists(checkpoint_file):
            return None
            
        try:
            with open(checkpoint_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
            logger.info(f"Loaded checkpoint from {checkpoint_file}")
            return data
        except Exception as e:
            logger.warning(f"Failed to load checkpoint: {e}")
            return None
    
    def _save_checkpoint(self, data: Dict[str, Any]):
        """Save checkpoint data for resuming extraction"""
        checkpoint_file = getattr(self.config, 'checkpoint_file', 'extraction_checkpoint.json')
        
        try:
            with open(checkpoint_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2, default=str)
        except Exception as e:
            logger.warning(f"Failed to save checkpoint: {e}")
    
    def _cleanup_checkpoint(self):
        """Remove checkpoint file when extraction is complete"""
        checkpoint_file = getattr(self.config, 'checkpoint_file', 'extraction_checkpoint.json')
        
        try:
            if os.path.exists(checkpoint_file):
                os.remove(checkpoint_file)
                logger.info("Checkpoint file cleaned up")
        except Exception as e:
            logger.warning(f"Failed to cleanup checkpoint: {e}")
    
    def _save_csv(self, albums: List[AlbumData], csv_path: str):
        """Save a CSV version for easy analysis"""
        import csv
        
        with open(csv_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.writer(f)
            writer.writerow(['artist_name', 'album_title', 'album_type', 'release_year', 'track_count', 'genres'])
            
            for album in albums:
                writer.writerow([
                    album.artist_name,
                    album.album_title, 
                    album.album_type,
                    album.release_year,
                    album.track_count,
                    '; '.join(album.genres)
                ])

def load_config(config_path: str) -> ExtractorConfig:
    """Load configuration from JSON file"""
    with open(config_path, 'r') as f:
        config_data = json.load(f)
    return ExtractorConfig(**config_data)

def main():
    parser = argparse.ArgumentParser(description="Extract MusicBrainz data for ML training")
    parser.add_argument('--mb-url', required=True, help='MusicBrainz instance URL')
    parser.add_argument('--mb-database-url', help='PostgreSQL connection string for direct DB access')
    parser.add_argument('--output', required=True, help='Output JSON file path')
    parser.add_argument('--max-albums', type=int, default=100000, help='Maximum albums to extract')
    parser.add_argument('--incremental-size', type=int, default=1000, help='Size of incremental batches')
    parser.add_argument('--config', help='Configuration file path')
    parser.add_argument('--batch-size', type=int, default=1000, help='Batch size for processing')
    parser.add_argument('--profile', choices=['quick_test', 'development', 'balanced', 'high_quality', 'exhaustive'],
                       help='Use predefined profile for quick setup')
    parser.add_argument('--resume', action='store_true', help='Resume from checkpoint if available')
    parser.add_argument('--test-connection', action='store_true', help='Test MusicBrainz connection and exit')
    
    args = parser.parse_args()
    
    # Load configuration
    if args.config:
        config = load_config(args.config)
    else:
        config = ExtractorConfig(
            mb_url=args.mb_url,
            mb_database_url=args.mb_database_url,
            max_albums=args.max_albums,
            batch_size=args.batch_size
        )
    
    # Extract data
    extractor = MusicBrainzExtractor(config)
    extractor.connect_to_database()
    
    albums = extractor.extract_albums_from_database()
    
    if albums:
        extractor.save_dataset(albums, args.output)
        logger.info(f"✅ Successfully extracted {len(albums)} albums")
        
        # Print statistics
        logger.info("Dataset Statistics:")
        logger.info(f"  Total albums: {len(albums)}")
        logger.info(f"  Unique artists: {len(set(a.artist_name for a in albums))}")
        logger.info(f"  Album types: {dict(sorted([(t, sum(1 for a in albums if a.album_type == t)) for t in set(a.album_type for a in albums)]))}")
        logger.info(f"  Year range: {min(int(a.release_year) for a in albums if a.release_year)} - {max(int(a.release_year) for a in albums if a.release_year)}")
    else:
        logger.error("❌ No albums extracted")
        sys.exit(1)

if __name__ == '__main__':
    main()