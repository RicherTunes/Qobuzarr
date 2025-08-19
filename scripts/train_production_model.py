#!/usr/bin/env python3
"""
Production Model Training for 500K+ Album Dataset

This script is optimized for training the baseline model that ships with Qobuzarr.
Designed for large-scale training on diverse music libraries.

Usage:
    python train_production_model.py --input massive_dataset.json --gpu --name QobuzarrBaseline_v2
    python train_production_model.py --lidarr-url http://localhost:8686 --lidarr-api-key KEY --combine-musicbrainz
"""

import argparse
import json
import logging
import os
import sys
import time
from datetime import datetime
from typing import List, Dict, Any, Tuple, Optional
import numpy as np
import pandas as pd
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from sklearn.model_selection import train_test_split, StratifiedKFold
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import classification_report, confusion_matrix
import requests
from tqdm import tqdm

# Configure logging for production training
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler(f'production_training_{datetime.now().strftime("%Y%m%d_%H%M%S")}.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

class ProductionMLModel(nn.Module):
    """Enhanced model architecture for production baseline"""
    
    def __init__(self, input_size: int = 30, num_classes: int = 3):
        super(ProductionMLModel, self).__init__()
        
        # Larger architecture for 500K+ training data
        self.feature_encoder = nn.Sequential(
            nn.Linear(input_size, 512),
            nn.BatchNorm1d(512),
            nn.ReLU(),
            nn.Dropout(0.2),
            
            nn.Linear(512, 256),
            nn.BatchNorm1d(256),
            nn.ReLU(),
            nn.Dropout(0.3),
            
            nn.Linear(256, 128),
            nn.BatchNorm1d(128),
            nn.ReLU(),
            nn.Dropout(0.3),
        )
        
        self.classifier = nn.Sequential(
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(64, num_classes)
        )
        
        # Initialize weights for production stability
        self._initialize_weights()
    
    def _initialize_weights(self):
        for module in self.modules():
            if isinstance(module, nn.Linear):
                nn.init.kaiming_normal_(module.weight, mode='fan_out', nonlinearity='relu')
                nn.init.constant_(module.bias, 0)
            elif isinstance(module, nn.BatchNorm1d):
                nn.init.constant_(module.weight, 1)
                nn.init.constant_(module.bias, 0)
    
    def forward(self, x):
        features = self.feature_encoder(x)
        return self.classifier(features)

class LidarrDataExtractor:
    """Extract training data from Lidarr API"""
    
    def __init__(self, lidarr_url: str, api_key: str):
        self.lidarr_url = lidarr_url.rstrip('/')
        self.api_key = api_key
        self.session = requests.Session()
        self.session.headers.update({
            'X-Api-Key': api_key,
            'Content-Type': 'application/json'
        })
    
    def extract_library_data(self) -> List[Dict[str, Any]]:
        """Extract complete library from Lidarr"""
        logger.info("Extracting library data from Lidarr...")
        
        try:
            # Get all artists
            artists_response = self.session.get(f"{self.lidarr_url}/api/v1/artist")
            artists_response.raise_for_status()
            artists = artists_response.json()
            
            logger.info(f"Found {len(artists)} artists in Lidarr library")
            
            all_albums = []
            
            for artist in tqdm(artists, desc="Processing artists"):
                try:
                    # Get albums for this artist
                    albums_response = self.session.get(
                        f"{self.lidarr_url}/api/v1/album", 
                        params={'artistId': artist['id']}
                    )
                    albums_response.raise_for_status()
                    albums = albums_response.json()
                    
                    for album in albums:
                        # Convert Lidarr album to training format
                        training_album = self._convert_lidarr_album(artist, album)
                        if training_album:
                            all_albums.append(training_album)
                            
                except Exception as e:
                    logger.warning(f"Failed to process artist {artist.get('artistName', 'Unknown')}: {e}")
                    continue
            
            logger.info(f"Extracted {len(all_albums)} albums from Lidarr library")
            return all_albums
            
        except Exception as e:
            logger.error(f"Failed to extract Lidarr data: {e}")
            raise
    
    def extract_search_history(self) -> List[Dict[str, Any]]:
        """Extract search patterns from Lidarr history"""
        logger.info("Extracting search history from Lidarr...")
        
        try:
            # Get search history
            history_response = self.session.get(
                f"{self.lidarr_url}/api/v1/history",
                params={'pageSize': 10000, 'eventType': 'albumSearchStarted'}
            )
            history_response.raise_for_status()
            history = history_response.json()
            
            search_patterns = []
            for entry in history.get('records', []):
                pattern = self._extract_search_pattern(entry)
                if pattern:
                    search_patterns.append(pattern)
            
            logger.info(f"Extracted {len(search_patterns)} search patterns")
            return search_patterns
            
        except Exception as e:
            logger.warning(f"Could not extract search history: {e}")
            return []
    
    def _convert_lidarr_album(self, artist: Dict, album: Dict) -> Optional[Dict[str, Any]]:
        """Convert Lidarr album format to training format"""
        try:
            return {
                "lidarr_id": album.get('id', 0),
                "artist_name": artist.get('artistName', ''),
                "artist_id": str(artist.get('foreignArtistId', '')),
                "album_title": album.get('title', ''),
                "album_title_clean": album.get('cleanTitle', album.get('title', '')),
                "album_type": album.get('albumType', 'Album'),
                "release_date": album.get('releaseDate', ''),
                "release_year": str(album.get('releaseDate', '')[:4]) if album.get('releaseDate') else '',
                "track_count": len(album.get('tracks', [])),
                "monitored": album.get('monitored', True),
                "search_query": f"{artist.get('artistName', '')} {album.get('title', '')}",
                "disambiguation": album.get('disambiguation', ''),
                "foreign_album_id": str(album.get('foreignAlbumId', '')),
                "genres": [g['name'] for g in album.get('genres', [])],
                "overview": album.get('overview', ''),
                "album_id": album.get('id', 0),
                "artist_metadata_id": artist.get('id', 0),
                # Lidarr-specific enrichment
                "quality_profile": album.get('qualityProfileId', 0),
                "metadata_profile": album.get('metadataProfileId', 0),
                "has_file": len(album.get('tracks', [])) > 0,
                "search_success_rate": self._calculate_search_success_rate(album)
            }
        except Exception as e:
            logger.warning(f"Failed to convert album: {e}")
            return None
    
    def _extract_search_pattern(self, history_entry: Dict) -> Optional[Dict[str, Any]]:
        """Extract search pattern from history entry"""
        try:
            return {
                "search_query": history_entry.get('sourceTitle', ''),
                "success": history_entry.get('successful', False),
                "event_type": history_entry.get('eventType', ''),
                "timestamp": history_entry.get('date', ''),
                "album_id": history_entry.get('albumId')
            }
        except:
            return None
    
    def _calculate_search_success_rate(self, album: Dict) -> float:
        """Estimate search success rate for album (placeholder)"""
        # This would require historical search data analysis
        # For now, return a reasonable default
        return 0.8

class ProductionTrainer:
    """Enhanced trainer for production model with 500K+ samples"""
    
    def __init__(self, config: Dict[str, Any]):
        self.config = config
        self.device = torch.device('cuda' if torch.cuda.is_available() and config.get('use_gpu', True) else 'cpu')
        
        logger.info(f"Using device: {self.device}")
        if self.device.type == 'cuda':
            logger.info(f"GPU: {torch.cuda.get_device_name()}")
            logger.info(f"VRAM: {torch.cuda.get_device_properties(0).total_memory / 1e9:.1f} GB")
    
    def train_production_model(self, combined_data: List[Dict[str, Any]], model_name: str) -> Dict[str, Any]:
        """Train production-quality model on massive dataset"""
        logger.info(f"Training production model '{model_name}' on {len(combined_data)} samples")
        
        # Enhanced feature extraction for production
        feature_extractor = self._create_production_feature_extractor()
        features = feature_extractor.extract_features(combined_data)
        
        # Generate labels using enhanced classifier
        classifier = self._create_production_classifier()
        labels = [classifier.classify_complexity(
            album.get('artist_name', ''), 
            album.get('album_title', ''),
            album  # Pass full album data for richer classification
        ) for album in combined_data]
        
        # Encode labels
        from sklearn.preprocessing import LabelEncoder
        label_encoder = LabelEncoder()
        encoded_labels = label_encoder.fit_transform(labels)
        
        # Log dataset statistics
        unique, counts = np.unique(encoded_labels, return_counts=True)
        class_distribution = dict(zip(label_encoder.classes_, counts))
        logger.info(f"Dataset distribution: {class_distribution}")
        
        # Split data with stratification
        X_train, X_val, y_train, y_val = train_test_split(
            features, encoded_labels, 
            test_size=0.1,  # Smaller validation set for large data
            random_state=42, 
            stratify=encoded_labels
        )
        
        logger.info(f"Training: {len(X_train)}, Validation: {len(X_val)}")
        
        # Create enhanced model for production
        model = ProductionMLModel(input_size=features.shape[1], num_classes=len(unique))
        model = model.to(self.device)
        
        # Production training configuration
        criterion = nn.CrossEntropyLoss()
        optimizer = optim.AdamW(
            model.parameters(), 
            lr=self.config.get('learning_rate', 0.001),
            weight_decay=1e-4
        )
        
        # Learning rate scheduler for large dataset
        scheduler = optim.lr_scheduler.CosineAnnealingLR(
            optimizer, 
            T_max=self.config.get('num_epochs', 100),
            eta_min=1e-6
        )
        
        # Train with enhanced monitoring
        training_results = self._train_with_monitoring(
            model, X_train, y_train, X_val, y_val,
            criterion, optimizer, scheduler,
            model_name
        )
        
        # Save production model
        self._save_production_model(
            model, feature_extractor, label_encoder, 
            training_results, model_name
        )
        
        return training_results
    
    def _create_production_feature_extractor(self):
        """Enhanced feature extractor for production use"""
        from train_ml_model import FeatureExtractor
        
        class ProductionFeatureExtractor(FeatureExtractor):
            def extract_features(self, albums: List[Dict[str, Any]]) -> np.ndarray:
                """Enhanced feature extraction with additional Lidarr-specific features"""
                features = []
                
                for album in albums:
                    artist = album.get('artist_name', '').lower()
                    title = album.get('album_title', '').lower()
                    
                    # Base features (25)
                    base_features = super().extract_features([album])[0]
                    
                    # Additional production features (5)
                    additional_features = [
                        # Lidarr-specific features
                        int(album.get('has_file', False)),
                        album.get('search_success_rate', 0.5),
                        album.get('quality_profile', 0) / 10.0,  # Normalize
                        len(album.get('genres', [])) / 5.0,  # Normalize
                        int(album.get('monitored', True))
                    ]
                    
                    # Combine features (30 total)
                    combined = np.concatenate([base_features, additional_features])
                    features.append(combined)
                
                features_array = np.array(features, dtype=np.float32)
                
                # Enhanced normalization for production
                if not hasattr(self, 'fitted') or not self.fitted:
                    features_array = self.scaler.fit_transform(features_array)
                    self.fitted = True
                else:
                    features_array = self.scaler.transform(features_array)
                
                return features_array
        
        return ProductionFeatureExtractor()
    
    def _create_production_classifier(self):
        """Enhanced classifier for production labeling"""
        from train_ml_model import ComplexityClassifier
        
        class ProductionClassifier(ComplexityClassifier):
            def classify_complexity(self, artist: str, album: str, full_album_data: Dict = None) -> str:
                """Enhanced classification using full album data"""
                base_complexity = super().classify_complexity(artist, album)
                
                if full_album_data:
                    # Use additional context for better classification
                    track_count = full_album_data.get('track_count', 0)
                    genres = full_album_data.get('genres', [])
                    has_file = full_album_data.get('has_file', False)
                    
                    # Adjust complexity based on additional context
                    if track_count > 30:  # Very long albums tend to be complex
                        return 'Complex'
                    elif 'classical' in [g.lower() for g in genres]:
                        return 'Complex'  # Classical is typically complex
                    elif not has_file and 'compilation' in album.lower():
                        return 'Complex'  # Missing compilations are hard to find
                
                return base_complexity
        
        return ProductionClassifier()
    
    def _train_with_monitoring(self, model, X_train, y_train, X_val, y_val,
                             criterion, optimizer, scheduler, model_name):
        """Enhanced training with production-level monitoring"""
        
        # Create data loaders with larger batch sizes for big dataset
        batch_size = min(self.config.get('batch_size', 512), len(X_train) // 100)
        
        from train_ml_model import QueryComplexityDataset
        train_dataset = QueryComplexityDataset(X_train, y_train)
        val_dataset = QueryComplexityDataset(X_val, y_val)
        
        train_loader = DataLoader(train_dataset, batch_size=batch_size, shuffle=True, num_workers=4)
        val_loader = DataLoader(val_dataset, batch_size=batch_size, shuffle=False, num_workers=4)
        
        # Training loop with enhanced monitoring
        best_val_acc = 0.0
        training_history = {
            'train_loss': [],
            'val_loss': [],
            'val_accuracy': [],
            'learning_rates': []
        }
        
        num_epochs = self.config.get('num_epochs', 100)
        patience = self.config.get('patience', 15)
        patience_counter = 0
        
        for epoch in range(num_epochs):
            # Training phase
            model.train()
            train_loss = 0.0
            
            train_pbar = tqdm(train_loader, desc=f'Epoch {epoch+1}/{num_epochs}')
            for batch_features, batch_labels in train_pbar:
                batch_features = batch_features.to(self.device)
                batch_labels = batch_labels.to(self.device)
                
                optimizer.zero_grad()
                outputs = model(batch_features)
                loss = criterion(outputs, batch_labels)
                loss.backward()
                
                # Gradient clipping for stability
                torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)
                
                optimizer.step()
                
                train_loss += loss.item()
                train_pbar.set_postfix({'Loss': f'{loss.item():.4f}'})
            
            avg_train_loss = train_loss / len(train_loader)
            
            # Validation phase
            model.eval()
            val_loss = 0.0
            correct = 0
            total = 0
            
            with torch.no_grad():
                for batch_features, batch_labels in val_loader:
                    batch_features = batch_features.to(self.device)
                    batch_labels = batch_labels.to(self.device)
                    
                    outputs = model(batch_features)
                    loss = criterion(outputs, batch_labels)
                    val_loss += loss.item()
                    
                    _, predicted = torch.max(outputs, 1)
                    total += batch_labels.size(0)
                    correct += (predicted == batch_labels).sum().item()
            
            avg_val_loss = val_loss / len(val_loader)
            val_accuracy = correct / total
            
            # Update learning rate
            scheduler.step()
            current_lr = optimizer.param_groups[0]['lr']
            
            # Record metrics
            training_history['train_loss'].append(avg_train_loss)
            training_history['val_loss'].append(avg_val_loss)
            training_history['val_accuracy'].append(val_accuracy)
            training_history['learning_rates'].append(current_lr)
            
            logger.info(f'Epoch {epoch+1}: Train Loss={avg_train_loss:.4f}, '
                       f'Val Loss={avg_val_loss:.4f}, Val Acc={val_accuracy:.4f}, LR={current_lr:.6f}')
            
            # Early stopping and model checkpointing
            if val_accuracy > best_val_acc:
                best_val_acc = val_accuracy
                patience_counter = 0
                
                # Save best model checkpoint
                torch.save({
                    'model_state_dict': model.state_dict(),
                    'epoch': epoch,
                    'val_accuracy': val_accuracy,
                    'model_name': model_name
                }, f'best_{model_name.lower()}_model.pth')
                
            else:
                patience_counter += 1
                
            if patience_counter >= patience:
                logger.info(f'Early stopping triggered after {epoch+1} epochs')
                break
        
        # Load best model
        checkpoint = torch.load(f'best_{model_name.lower()}_model.pth')
        model.load_state_dict(checkpoint['model_state_dict'])
        
        return {
            'best_val_accuracy': best_val_acc,
            'training_history': training_history,
            'total_epochs': epoch + 1,
            'model_name': model_name
        }
    
    def _save_production_model(self, model, feature_extractor, label_encoder, 
                             training_results, model_name):
        """Save production model with all components"""
        
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        model_file = f'{model_name}_{timestamp}.pth'
        
        torch.save({
            'model_state_dict': model.state_dict(),
            'model_config': {
                'input_size': 30,
                'num_classes': 3,
                'architecture': 'ProductionMLModel'
            },
            'feature_extractor': feature_extractor,
            'label_encoder': label_encoder,
            'training_results': training_results,
            'model_name': model_name,
            'creation_date': timestamp,
            'training_data_size': len(training_results.get('training_history', {}).get('train_loss', [])),
            'best_accuracy': training_results['best_val_accuracy']
        }, model_file)
        
        logger.info(f"Production model saved: {model_file}")
        logger.info(f"Best validation accuracy: {training_results['best_val_accuracy']:.4f}")
        
        return model_file

def combine_datasets(musicbrainz_data: List[Dict], lidarr_data: List[Dict]) -> List[Dict]:
    """Intelligently combine MusicBrainz and Lidarr datasets"""
    logger.info(f"Combining datasets: MB={len(musicbrainz_data)}, Lidarr={len(lidarr_data)}")
    
    # Remove duplicates based on artist + album
    combined = {}
    
    # Add MusicBrainz data (baseline knowledge)
    for album in musicbrainz_data:
        key = f"{album.get('artist_name', '').lower()}|||{album.get('album_title', '').lower()}"
        combined[key] = album
    
    # Add/update with Lidarr data (prioritize user's actual library)
    for album in lidarr_data:
        key = f"{album.get('artist_name', '').lower()}|||{album.get('album_title', '').lower()}"
        if key in combined:
            # Enhance existing entry with Lidarr data
            combined[key].update({
                'has_file': album.get('has_file', False),
                'search_success_rate': album.get('search_success_rate', 0.8),
                'quality_profile': album.get('quality_profile', 0),
                'lidarr_priority': True
            })
        else:
            # Add new entry from Lidarr
            album['lidarr_priority'] = True
            combined[key] = album
    
    result = list(combined.values())
    logger.info(f"Combined dataset: {len(result)} unique albums")
    
    return result

def main():
    parser = argparse.ArgumentParser(description="Train production model for Qobuzarr baseline")
    parser.add_argument('--input', help='MusicBrainz dataset JSON file')
    parser.add_argument('--lidarr-url', help='Lidarr instance URL')
    parser.add_argument('--lidarr-api-key', help='Lidarr API key')
    parser.add_argument('--combine-musicbrainz', action='store_true',
                       help='Combine with MusicBrainz data')
    parser.add_argument('--name', default='QobuzarrBaseline',
                       help='Model name for production')
    parser.add_argument('--config', help='Training configuration file')
    parser.add_argument('--gpu', action='store_true', help='Use GPU acceleration')
    parser.add_argument('--epochs', type=int, default=100, help='Training epochs')
    parser.add_argument('--batch-size', type=int, default=512, help='Batch size')
    
    args = parser.parse_args()
    
    # Load configuration
    config = {
        'use_gpu': args.gpu,
        'num_epochs': args.epochs,
        'batch_size': args.batch_size,
        'learning_rate': 0.001,
        'patience': 15
    }
    
    if args.config:
        with open(args.config, 'r') as f:
            config.update(json.load(f))
    
    # Prepare datasets
    combined_data = []
    
    # Load MusicBrainz data if provided
    if args.input:
        logger.info(f"Loading MusicBrainz data from {args.input}")
        with open(args.input, 'r', encoding='utf-8') as f:
            mb_data = json.load(f)
        musicbrainz_albums = mb_data.get('albums', [])
        logger.info(f"Loaded {len(musicbrainz_albums)} MusicBrainz albums")
    else:
        musicbrainz_albums = []
    
    # Extract Lidarr data if requested
    lidarr_albums = []
    if args.lidarr_url and args.lidarr_api_key:
        logger.info("Extracting data from Lidarr...")
        extractor = LidarrDataExtractor(args.lidarr_url, args.lidarr_api_key)
        lidarr_albums = extractor.extract_library_data()
        
        # Also get search history for additional insights
        search_history = extractor.extract_search_history()
        logger.info(f"Extracted {len(search_history)} search patterns")
    
    # Combine datasets intelligently
    if musicbrainz_albums and lidarr_albums:
        combined_data = combine_datasets(musicbrainz_albums, lidarr_albums)
    elif lidarr_albums:
        combined_data = lidarr_albums
    elif musicbrainz_albums:
        combined_data = musicbrainz_albums
    else:
        logger.error("No training data provided")
        sys.exit(1)
    
    if len(combined_data) < 1000:
        logger.error(f"Insufficient training data: {len(combined_data)} albums (need 1000+)")
        sys.exit(1)
    
    # Train production model
    trainer = ProductionTrainer(config)
    results = trainer.train_production_model(combined_data, args.name)
    
    logger.info("🎉 Production model training completed!")
    logger.info(f"Model: {args.name}")
    logger.info(f"Training data: {len(combined_data)} albums")
    logger.info(f"Best accuracy: {results['best_val_accuracy']:.4f}")
    
    # Export to C# for production use
    logger.info("Exporting to C# production code...")
    import subprocess
    
    model_file = f'best_{args.name.lower()}_model.pth'
    csharp_file = f'{args.name}MLOptimizer.cs'
    
    subprocess.run([
        sys.executable, 'export_model_to_csharp.py',
        '--model', model_file,
        '--output', csharp_file,
        '--class', f'{args.name}MLOptimizer'
    ])
    
    logger.info(f"✅ Production model ready: {csharp_file}")

if __name__ == '__main__':
    main()