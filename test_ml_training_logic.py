#!/usr/bin/env python3
"""
Test ML training logic without PyTorch dependencies
This validates the feature extraction and classification components
"""

import json
import sys
import logging
from typing import List, Dict, Any
import numpy as np
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler, LabelEncoder
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import accuracy_score, classification_report, confusion_matrix

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class FeatureExtractor:
    """Extract features from album metadata"""
    
    def __init__(self):
        self.scaler = StandardScaler()
        self.fitted = False
    
    def extract_features(self, albums: List[Dict[str, Any]]) -> np.ndarray:
        """Extract numerical features from album data"""
        features = []
        
        for album in albums:
            artist = album.get('artist_name', '').lower()
            title = album.get('album_title', '').lower()
            
            # Text-based features
            album_features = [
                len(artist.split()),  # Artist word count
                len(title.split()),   # Album word count
                len(artist),          # Artist character length
                len(title),           # Album character length
                
                # Pattern detection features
                int('(' in title or '[' in title),  # Has brackets/parentheses
                int(any(term in title for term in ['remaster', 'deluxe', 'special', 'anniversary'])),  # Special edition
                int('various artists' in artist.lower() or 'compilation' in title.lower()),  # Compilation
                int(any(year in title for year in ['19', '20'])),  # Has year in title
                int(any(term in title for term in ['live', 'concert', 'unplugged'])),  # Live recording
                int(any(term in title for term in [' ep', '(ep)', 'single'])),  # EP/Single
                
                # Album metadata features
                album.get('track_count', 0) / 50.0,  # Normalized track count
                len(album.get('genres', [])) / 5.0,  # Normalized genre count
                int(album.get('album_type', '').lower() == 'compilation'),
                
                # Release year features (normalized)
                self._normalize_year(album.get('release_year', '0')),
                
                # Text complexity features
                title.count(' '),  # Number of spaces (word separators)
                sum(1 for c in title if not c.isalnum() and not c.isspace()),  # Special character count
                int(len(title.split()) > 5),  # Long title indicator
                int('&' in artist or 'feat' in artist or 'ft.' in artist),  # Featured artists
                
                # Additional pattern features
                int('vol' in title.lower() or 'volume' in title.lower()),  # Volume/series
                int('part' in title.lower() or 'pt.' in title.lower()),  # Part of series
                int('disc' in title.lower() or 'cd' in title.lower()),  # Multi-disc
                int('box' in title.lower() or 'set' in title.lower()),  # Box set
                int('best' in title.lower() or 'greatest' in title.lower()),  # Greatest hits
                int('complete' in title.lower() or 'collection' in title.lower()),  # Complete collection
                int('soundtrack' in title.lower() or 'ost' in title.lower()),  # Soundtrack
            ]
            
            features.append(album_features)
        
        features_array = np.array(features, dtype=np.float32)
        
        # Normalize features
        if not self.fitted:
            features_array = self.scaler.fit_transform(features_array)
            self.fitted = True
        else:
            features_array = self.scaler.transform(features_array)
        
        return features_array
    
    def _normalize_year(self, year_str: str) -> float:
        """Normalize release year to 0-1 range"""
        try:
            year = int(str(year_str)[:4]) if year_str else 0
            if year < 1900:
                return 0.0
            return min((year - 1900) / 124.0, 1.0)  # Normalize 1900-2024 to 0-1
        except:
            return 0.0

class ComplexityClassifier:
    """Rule-based complexity classifier for generating training labels"""
    
    def classify_complexity(self, artist: str, album: str) -> str:
        """Classify query complexity based on heuristics"""
        artist_lower = artist.lower() if artist else ""
        album_lower = album.lower() if album else ""
        
        # Complex patterns
        complexity_indicators = [
            "various artists" in artist_lower,
            "compilation" in album_lower,
            "greatest hits" in album_lower,
            "best of" in album_lower,
            "complete" in album_lower,
            "collection" in album_lower,
            "box set" in album_lower,
            "anthology" in album_lower,
            ("(" in album_lower or "[" in album_lower) and any(x in album_lower for x in ["deluxe", "remaster", "special", "anniversary"]),
            len(album.split()) > 6,
            album_lower.count("(") > 1,
            "soundtrack" in album_lower,
        ]
        
        if sum(complexity_indicators) >= 2:
            return "Complex"
        
        # Simple patterns  
        simple_indicators = [
            len(album.split()) <= 2,
            not ("(" in album_lower or "[" in album_lower),
            len(album_lower) < 20,
            not any(x in album_lower for x in ["remaster", "deluxe", "special", "edition", "vol", "part"]),
        ]
        
        if sum(simple_indicators) >= 3:
            return "Simple"
        
        return "Medium"

def test_feature_extraction(albums: List[Dict[str, Any]]):
    """Test feature extraction"""
    logger.info("Testing feature extraction...")
    
    extractor = FeatureExtractor()
    features = extractor.extract_features(albums)
    
    logger.info(f"Extracted features shape: {features.shape}")
    logger.info(f"Feature statistics:")
    logger.info(f"  Mean: {features.mean():.3f}")
    logger.info(f"  Std: {features.std():.3f}")
    logger.info(f"  Min: {features.min():.3f}")
    logger.info(f"  Max: {features.max():.3f}")
    
    return features

def test_classification(albums: List[Dict[str, Any]]):
    """Test classification logic"""
    logger.info("Testing classification...")
    
    classifier = ComplexityClassifier()
    labels = []
    
    for album in albums:
        artist = album.get('artist_name', '')
        title = album.get('album_title', '')
        complexity = classifier.classify_complexity(artist, title)
        labels.append(complexity)
        logger.info(f"  {artist} - {title}: {complexity}")
    
    # Count distribution
    label_counts = {}
    for label in labels:
        label_counts[label] = label_counts.get(label, 0) + 1
    
    logger.info(f"Label distribution: {label_counts}")
    return labels

def test_sklearn_training(features: np.ndarray, labels: List[str]):
    """Test training with scikit-learn as a proxy for PyTorch"""
    logger.info("Testing ML training with scikit-learn...")
    
    # Encode labels
    label_encoder = LabelEncoder()
    encoded_labels = label_encoder.fit_transform(labels)
    
    if len(set(encoded_labels)) < 2:
        logger.warning("Not enough classes for training - adding synthetic data")
        # Add some synthetic samples for testing
        synthetic_features = features[:3].copy()
        synthetic_features[:, 0] *= 2  # Modify features
        features = np.vstack([features, synthetic_features])
        synthetic_labels = ['Simple', 'Medium', 'Complex'][:len(synthetic_features)]
        labels.extend(synthetic_labels)
        encoded_labels = label_encoder.fit_transform(labels)
    
    # Split data (if we have enough samples and balanced classes)
    min_class_count = min([list(encoded_labels).count(i) for i in set(encoded_labels)])
    
    if len(features) >= 4 and min_class_count >= 2:
        X_train, X_test, y_train, y_test = train_test_split(
            features, encoded_labels, test_size=0.3, random_state=42, stratify=encoded_labels
        )
    elif len(features) >= 4:
        X_train, X_test, y_train, y_test = train_test_split(
            features, encoded_labels, test_size=0.3, random_state=42
        )
    else:
        # Use all data for both training and testing (just for validation)
        X_train = X_test = features
        y_train = y_test = encoded_labels
    
    logger.info(f"Training samples: {len(X_train)}, Test samples: {len(X_test)}")
    
    # Train Random Forest classifier
    model = RandomForestClassifier(n_estimators=50, random_state=42, max_depth=5)
    model.fit(X_train, y_train)
    
    # Evaluate
    train_pred = model.predict(X_train)
    test_pred = model.predict(X_test)
    
    train_acc = accuracy_score(y_train, train_pred)
    test_acc = accuracy_score(y_test, test_pred)
    
    logger.info(f"Training accuracy: {train_acc:.3f}")
    logger.info(f"Test accuracy: {test_acc:.3f}")
    
    # Feature importance
    feature_names = [
        'artist_words', 'album_words', 'artist_chars', 'album_chars',
        'has_brackets', 'special_edition', 'compilation', 'has_year',
        'live_recording', 'ep_single', 'track_count_norm', 'genre_count_norm',
        'is_compilation', 'release_year_norm', 'word_spaces', 'special_chars',
        'long_title', 'featured_artists', 'volume_series', 'part_series',
        'multi_disc', 'box_set', 'greatest_hits', 'complete_collection', 'soundtrack'
    ]
    
    importances = model.feature_importances_
    important_features = sorted(zip(feature_names, importances), key=lambda x: x[1], reverse=True)
    
    logger.info("Top 10 most important features:")
    for i, (name, importance) in enumerate(important_features[:10]):
        logger.info(f"  {i+1:2d}. {name:<20}: {importance:.3f}")
    
    # Classification report
    if len(set(y_test)) > 1:
        logger.info("Classification Report:")
        report = classification_report(y_test, test_pred, target_names=label_encoder.classes_)
        print(report)
    
    return model, test_acc

def load_album_data(file_path: str) -> List[Dict[str, Any]]:
    """Load album data from JSON file"""
    with open(file_path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    return data.get('albums', [])

def main():
    """Main test function"""
    logger.info("Starting ML Training Logic Test")
    logger.info("=" * 50)
    
    # Load test data
    try:
        albums = load_album_data('test_small_dataset.json')
        logger.info(f"Loaded {len(albums)} test albums")
    except FileNotFoundError:
        logger.error("Test dataset not found: ../test_small_dataset.json")
        sys.exit(1)
    
    if len(albums) < 3:
        logger.error("Need at least 3 albums for testing")
        sys.exit(1)
    
    # Test feature extraction
    features = test_feature_extraction(albums)
    
    # Test classification
    labels = test_classification(albums)
    
    # Test ML training (using scikit-learn as proxy)
    model, accuracy = test_sklearn_training(features, labels)
    
    # Summary
    logger.info("\n" + "=" * 50)
    logger.info("Test Summary")
    logger.info("=" * 50)
    logger.info(f"Albums processed: {len(albums)}")
    logger.info(f"Features extracted: {features.shape[1]}")
    logger.info(f"Classes identified: {len(set(labels))}")
    logger.info(f"Model accuracy: {accuracy:.3f}")
    
    if accuracy > 0.5:
        logger.info("SUCCESS: ML training logic is working correctly!")
        return True
    else:
        logger.warning("WARNING: Low accuracy - may need more diverse training data")
        return False

if __name__ == '__main__':
    success = main()
    sys.exit(0 if success else 1)