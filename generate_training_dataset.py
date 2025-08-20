#!/usr/bin/env python3
"""
Generate a larger synthetic training dataset for ML model training
Based on real music patterns and album naming conventions
"""

import json
import random
from typing import List, Dict, Any

# Real artist and album patterns for generating diverse training data
ARTISTS_SIMPLE = [
    "The Beatles", "Led Zeppelin", "Pink Floyd", "Queen", "AC/DC", "U2", "Radiohead", 
    "Nirvana", "Pearl Jam", "Metallica", "Iron Maiden", "Black Sabbath", "Deep Purple",
    "The Rolling Stones", "The Who", "The Doors", "The Kinks", "The Cure", "Blur", "Oasis",
    "Coldplay", "Muse", "Arctic Monkeys", "The Strokes", "Foo Fighters", "Red Hot Chili Peppers",
    "Green Day", "Linkin Park", "System of a Down", "Tool", "The White Stripes", "Franz Ferdinand",
    "Bob Dylan", "Neil Young", "David Bowie", "Prince", "Michael Jackson", "Madonna", "Whitney Houston",
    "Mariah Carey", "Celine Dion", "Alanis Morissette", "Tori Amos", "Björk", "Kate Bush",
    "Miles Davis", "John Coltrane", "Bill Evans", "Keith Jarrett", "Herbie Hancock", "Weather Report",
    "Chick Corea", "Pat Metheny", "Brad Mehldau", "Esperanza Spalding", "Robert Glasper",
    "Bach", "Mozart", "Beethoven", "Chopin", "Brahms", "Debussy", "Ravel", "Stravinsky",
    "Mahler", "Tchaikovsky", "Vivaldi", "Handel", "Schubert", "Schumann", "Liszt"
]

ARTISTS_COMPILATION = [
    "Various Artists", "Multiple Artists", "Compilation", "Original Soundtrack", 
    "Original Cast", "Studio Cast", "London Symphony Orchestra", "Berlin Philharmonic",
    "Vienna Philharmonic", "Royal Philharmonic Orchestra", "Boston Symphony Orchestra"
]

ALBUM_TITLES_SIMPLE = [
    "Abbey Road", "The Wall", "Dark Side of the Moon", "Led Zeppelin IV", "Nevermind",
    "OK Computer", "Ten", "Master of Puppets", "The Number of the Beast", "Paranoid",
    "Kind of Blue", "A Love Supreme", "Blue Train", "Giant Steps", "Waltz for Debby",
    "Time Out", "Head Hunters", "Bitches Brew", "In a Silent Way", "Birth of the Cool",
    "The Well-Tempered Clavier", "Brandenburg Concertos", "Symphony No. 9", "Moonlight Sonata",
    "The Four Seasons", "Water Music", "Carmina Burana", "Bolero", "Pictures at an Exhibition",
    "Thriller", "Purple Rain", "Like a Virgin", "The Bodyguard", "Jagged Little Pill",
    "Tapestry", "Blue", "Hounds of Love", "The Kick Inside", "Horses", "Patti Smith"
]

ALBUM_MODIFIERS_COMPLEX = [
    "Deluxe Edition", "Special Edition", "Anniversary Edition", "Remastered", "Expanded Edition",
    "Collector's Edition", "Limited Edition", "Director's Cut", "Extended Version", "Complete Edition",
    "Super Deluxe Edition", "Legacy Edition", "Diamond Edition", "Platinum Edition", "Gold Edition",
    "Box Set", "Complete Collection", "The Ultimate Collection", "Essential Collection",
    "Greatest Hits", "Best Of", "The Very Best Of", "Anthology", "Chronicles", "Retrospective"
]

YEARS = list(range(1960, 2025))
REMASTER_YEARS = list(range(1995, 2025))

GENRES = [
    ["Rock"], ["Pop"], ["Jazz"], ["Classical"], ["Electronic"], ["Hip Hop"], ["R&B"], ["Country"],
    ["Folk"], ["Blues"], ["Reggae"], ["Punk"], ["Metal"], ["Alternative"], ["Indie"],
    ["Rock", "Pop"], ["Jazz", "Fusion"], ["Classical", "Orchestral"], ["Electronic", "Ambient"],
    ["Hip Hop", "R&B"], ["Country", "Folk"], ["Blues", "Rock"], ["Punk", "Alternative"],
    ["Metal", "Hard Rock"], ["Pop", "Dance"], ["Jazz", "Blues"], ["Folk", "Acoustic"]
]

def generate_simple_album(album_id: int) -> Dict[str, Any]:
    """Generate a simple album (single artist, straightforward title)"""
    artist = random.choice(ARTISTS_SIMPLE)
    title = random.choice(ALBUM_TITLES_SIMPLE)
    year = random.choice(YEARS)
    tracks = random.randint(8, 15)
    genres = random.choice(GENRES)
    
    return {
        "artist_name": artist,
        "album_title": title,
        "album_id": f"simple-{album_id}",
        "artist_id": f"artist-simple-{album_id}",
        "release_year": str(year),
        "track_count": tracks,
        "genres": genres,
        "album_type": "Album"
    }

def generate_medium_album(album_id: int) -> Dict[str, Any]:
    """Generate a medium complexity album (some modifiers, longer titles)"""
    artist = random.choice(ARTISTS_SIMPLE)
    base_title = random.choice(ALBUM_TITLES_SIMPLE)
    year = random.choice(YEARS)
    tracks = random.randint(10, 20)
    genres = random.choice(GENRES)
    
    # Add some complexity
    modifiers = []
    if random.random() < 0.3:
        modifiers.append(f"({random.choice(REMASTER_YEARS)} Remaster)")
    if random.random() < 0.2:
        modifiers.append("Live")
    if random.random() < 0.1:
        modifiers.append("Acoustic")
    
    title = base_title
    if modifiers:
        title += " " + " ".join(modifiers)
    
    return {
        "artist_name": artist,
        "album_title": title,
        "album_id": f"medium-{album_id}",
        "artist_id": f"artist-medium-{album_id}",
        "release_year": str(year),
        "track_count": tracks,
        "genres": genres,
        "album_type": "Album"
    }

def generate_complex_album(album_id: int) -> Dict[str, Any]:
    """Generate a complex album (compilations, deluxe editions, etc.)"""
    complexity_type = random.choice(['compilation', 'deluxe', 'box_set', 'soundtrack'])
    year = random.choice(YEARS)
    genres = random.choice(GENRES)
    
    if complexity_type == 'compilation':
        artist = random.choice(ARTISTS_COMPILATION)
        title_options = [
            f"Greatest Hits of the {random.choice(['60s', '70s', '80s', '90s', '2000s'])}",
            f"The Best of {random.choice(['Rock', 'Pop', 'Jazz', 'Classical'])}",
            f"Ultimate {random.choice(['Love Songs', 'Dance Hits', 'Rock Anthems'])} Collection",
            f"Now That's What I Call Music! {random.randint(1, 50)}",
            f"Billboard Top {random.choice(['100', '40'])} Hits {year}",
            f"The Complete {random.choice(['Mozart', 'Beethoven', 'Bach'])} Collection"
        ]
        title = random.choice(title_options)
        tracks = random.randint(30, 80)
        album_type = "Compilation"
        
    elif complexity_type == 'deluxe':
        artist = random.choice(ARTISTS_SIMPLE)
        base_title = random.choice(ALBUM_TITLES_SIMPLE)
        modifier = random.choice(ALBUM_MODIFIERS_COMPLEX)
        title = f"{base_title} ({modifier})"
        tracks = random.randint(15, 35)
        album_type = "Album"
        
    elif complexity_type == 'box_set':
        artist = random.choice(ARTISTS_SIMPLE)
        title_options = [
            f"The Complete Studio Albums",
            f"The {artist} Box Set",
            f"Complete Recordings {random.randint(1960, 1990)}-{random.randint(1995, 2020)}",
            f"The Ultimate {artist} Experience",
            f"{artist}: The Collection"
        ]
        title = random.choice(title_options)
        tracks = random.randint(50, 150)
        album_type = "Compilation"
        
    else:  # soundtrack
        artist = "Original Soundtrack"
        movie_titles = [
            "The Lord of the Rings", "Star Wars", "The Matrix", "Inception", "Interstellar",
            "Pulp Fiction", "The Godfather", "Goodfellas", "Scarface", "The Dark Knight",
            "Guardians of the Galaxy", "Black Panther", "Wonder Woman", "Avengers", "Iron Man"
        ]
        title = f"{random.choice(movie_titles)} (Original Motion Picture Soundtrack)"
        tracks = random.randint(15, 40)
        album_type = "Soundtrack"
        artist = "Various Artists"
    
    return {
        "artist_name": artist,
        "album_title": title,
        "album_id": f"complex-{album_id}",
        "artist_id": f"artist-complex-{album_id}",
        "release_year": str(year),
        "track_count": tracks,
        "genres": genres,
        "album_type": album_type
    }

def generate_training_dataset(num_albums: int = 200) -> Dict[str, Any]:
    """Generate a balanced training dataset"""
    albums = []
    
    # Generate balanced dataset: 40% simple, 35% medium, 25% complex
    num_simple = int(num_albums * 0.40)
    num_medium = int(num_albums * 0.35) 
    num_complex = num_albums - num_simple - num_medium
    
    print(f"Generating {num_albums} albums:")
    print(f"  Simple: {num_simple}")
    print(f"  Medium: {num_medium}")
    print(f"  Complex: {num_complex}")
    
    # Generate albums
    album_id = 1
    
    for _ in range(num_simple):
        albums.append(generate_simple_album(album_id))
        album_id += 1
    
    for _ in range(num_medium):
        albums.append(generate_medium_album(album_id))
        album_id += 1
    
    for _ in range(num_complex):
        albums.append(generate_complex_album(album_id))
        album_id += 1
    
    # Shuffle to randomize order
    random.shuffle(albums)
    
    return {
        "metadata": {
            "generated_at": "2025-08-19T11:45:00Z",
            "total_albums": len(albums),
            "distribution": {
                "simple": num_simple,
                "medium": num_medium,
                "complex": num_complex
            },
            "description": "Synthetic training dataset for ML query complexity classification"
        },
        "albums": albums
    }

def main():
    """Generate training datasets of various sizes"""
    datasets = [
        (50, "small_training_dataset.json"),
        (200, "medium_training_dataset.json"), 
        (500, "large_training_dataset.json")
    ]
    
    for size, filename in datasets:
        print(f"\nGenerating {filename} with {size} albums...")
        dataset = generate_training_dataset(size)
        
        with open(filename, 'w', encoding='utf-8') as f:
            json.dump(dataset, f, indent=2, ensure_ascii=False)
        
        print(f"Saved {filename}")
        
        # Verify the dataset
        with open(filename) as f:
            data = json.load(f)
            albums = data.get('albums', [])
            print(f"  Verified: {len(albums)} albums")
            
            # Count by artist patterns
            simple_count = sum(1 for a in albums if a['artist_name'] in ARTISTS_SIMPLE)
            compilation_count = sum(1 for a in albums if 'Various' in a['artist_name'] or 'Original' in a['artist_name'])
            
            print(f"  Regular artists: {simple_count}")
            print(f"  Compilation artists: {compilation_count}")

if __name__ == "__main__":
    random.seed(42)  # For reproducible results
    main()