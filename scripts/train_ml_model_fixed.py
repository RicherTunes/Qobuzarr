#!/usr/bin/env python3
"""
ML Model Training Script for Qobuzarr Query Complexity Classification

This script trains a PyTorch neural network to classify music search queries
into complexity categories: Simple, Medium, Complex.

Usage:
    python train_ml_model_fixed.py --input album_data.json --output model.pth
"""

import argparse
import json
import logging
import os
import sys
from datetime import datetime
from typing import List, Dict, Any, Tuple
import numpy as np
import pandas as pd

# Conditional imports for ML dependencies
try:
    import torch
    import torch.nn as nn
    import torch.optim as optim
    import torch.nn.functional as F
    from torch.utils.data import Dataset, DataLoader
    from sklearn.model_selection import train_test_split, StratifiedKFold
    from sklearn.preprocessing import StandardScaler, LabelEncoder
    from sklearn.metrics import accuracy_score, classification_report, confusion_matrix
    import matplotlib.pyplot as plt
    import seaborn as sns
    from tqdm import tqdm
    
    ML_AVAILABLE = True
except ImportError as e:
    print(f"Warning: ML dependencies not available: {e}")
    print("Install with: pip install torch scikit-learn matplotlib seaborn tqdm")
    ML_AVAILABLE = False

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler(f'training_{datetime.now().strftime("%Y%m%d_%H%M%S")}.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

class QueryComplexityDataset(Dataset):
    """Dataset for query complexity classification"""
    
    def __init__(self, features: np.ndarray, labels: np.ndarray):
        self.features = torch.FloatTensor(features)
        self.labels = torch.LongTensor(labels)
    
    def __len__(self):
        return len(self.features)
    
    def __getitem__(self, idx):
        return self.features[idx], self.labels[idx]

class QueryComplexityModel(nn.Module):
    """Neural network for query complexity classification"""
    
    def __init__(self, input_size: int = 25, hidden_sizes: List[int] = [128, 64, 32], 
                 num_classes: int = 3, dropout_rate: float = 0.3, use_batch_norm: bool = True):
        super(QueryComplexityModel, self).__init__()
        
        layers = []
        prev_size = input_size
        
        # Add hidden layers
        for hidden_size in hidden_sizes:
            layers.append(nn.Linear(prev_size, hidden_size))
            if use_batch_norm:
                layers.append(nn.BatchNorm1d(hidden_size))
            layers.append(nn.ReLU())
            layers.append(nn.Dropout(dropout_rate))
            prev_size = hidden_size
        
        # Final classification layer
        layers.append(nn.Linear(prev_size, num_classes))
        
        self.network = nn.Sequential(*layers)
    
    def forward(self, x):
        return self.network(x)

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

class ModelTrainer:
    """Main trainer class for the ML model"""
    
    def __init__(self, device: str = 'cpu', batch_size: int = 256, 
                 learning_rate: float = 0.001, num_epochs: int = 100):
        self.device = torch.device(device)
        self.batch_size = batch_size
        self.learning_rate = learning_rate
        self.num_epochs = num_epochs
        
        self.feature_extractor = FeatureExtractor()
        self.label_encoder = LabelEncoder()
        
        logger.info(f"Using device: {self.device}")
    
    def prepare_data(self, albums: List[Dict[str, Any]]) -> Tuple[DataLoader, DataLoader, DataLoader]:
        """Prepare training, validation, and test data loaders"""
        logger.info("Preparing data...")
        
        # Extract features
        features = self.feature_extractor.extract_features(albums)
        
        # Generate labels using rule-based classifier
        classifier = ComplexityClassifier()
        labels = [classifier.classify_complexity(
            album.get('artist_name', ''), 
            album.get('album_title', '')
        ) for album in albums]
        
        # Encode labels
        encoded_labels = self.label_encoder.fit_transform(labels)
        
        # Log dataset statistics
        unique, counts = np.unique(encoded_labels, return_counts=True)
        class_distribution = dict(zip(self.label_encoder.classes_, counts))
        logger.info(f"Dataset distribution: {class_distribution}")
        
        # Split data (handle small datasets)
        min_class_count = min([list(encoded_labels).count(i) for i in set(encoded_labels)])
        
        if len(features) >= 6 and min_class_count >= 3:
            # Full stratified split
            X_train, X_temp, y_train, y_temp = train_test_split(
                features, encoded_labels, test_size=0.3, random_state=42, stratify=encoded_labels
            )
            X_val, X_test, y_val, y_test = train_test_split(
                X_temp, y_temp, test_size=0.5, random_state=42, stratify=y_temp
            )
        elif len(features) >= 6:
            # Non-stratified split
            X_train, X_temp, y_train, y_temp = train_test_split(
                features, encoded_labels, test_size=0.3, random_state=42
            )
            X_val, X_test, y_val, y_test = train_test_split(
                X_temp, y_temp, test_size=0.5, random_state=42
            )
        else:
            # Too small - use simple splits
            n = len(features)
            train_size = max(1, int(n * 0.7))
            val_size = max(1, int(n * 0.2))
            
            X_train, y_train = features[:train_size], encoded_labels[:train_size]
            X_val, y_val = features[train_size:train_size+val_size], encoded_labels[train_size:train_size+val_size]
            X_test, y_test = features[train_size+val_size:], encoded_labels[train_size+val_size:]
            
            # Ensure we have at least 1 sample in each split
            if len(X_test) == 0:
                X_test, y_test = X_val[-1:], y_val[-1:]
                X_val, y_val = X_val[:-1], y_val[:-1]
        
        logger.info(f"Train: {len(X_train)}, Val: {len(X_val)}, Test: {len(X_test)}")
        
        # Create data loaders
        train_dataset = QueryComplexityDataset(X_train, y_train)
        val_dataset = QueryComplexityDataset(X_val, y_val)
        test_dataset = QueryComplexityDataset(X_test, y_test)
        
        train_loader = DataLoader(train_dataset, batch_size=self.batch_size, shuffle=True)
        val_loader = DataLoader(val_dataset, batch_size=self.batch_size, shuffle=False)
        test_loader = DataLoader(test_dataset, batch_size=self.batch_size, shuffle=False)
        
        return train_loader, val_loader, test_loader
    
    def train_model(self, train_loader: DataLoader, val_loader: DataLoader) -> nn.Module:
        """Train the neural network model"""
        logger.info("Starting model training...")
        
        # Initialize model (disable BatchNorm for small datasets)
        use_batch_norm = self.batch_size > 4
        model = QueryComplexityModel(
            input_size=25,
            hidden_sizes=[64, 32, 16] if self.batch_size <= 4 else [128, 64, 32],
            num_classes=len(self.label_encoder.classes_),
            dropout_rate=0.3,
            use_batch_norm=use_batch_norm
        ).to(self.device)
        
        # Loss function and optimizer
        criterion = nn.CrossEntropyLoss()
        optimizer = optim.AdamW(model.parameters(), lr=self.learning_rate, weight_decay=1e-4)
        scheduler = optim.lr_scheduler.ReduceLROnPlateau(optimizer, patience=10, factor=0.5)
        
        # Training loop
        train_losses = []
        val_losses = []
        val_accuracies = []
        best_val_acc = 0.0
        patience = 15
        patience_counter = 0
        
        for epoch in range(self.num_epochs):
            # Training phase
            model.train()
            train_loss = 0.0
            
            train_progress = tqdm(train_loader, desc=f'Epoch {epoch+1}/{self.num_epochs}')
            for batch_features, batch_labels in train_progress:
                batch_features = batch_features.to(self.device)
                batch_labels = batch_labels.to(self.device)
                
                optimizer.zero_grad()
                outputs = model(batch_features)
                loss = criterion(outputs, batch_labels)
                loss.backward()
                optimizer.step()
                
                train_loss += loss.item()
                train_progress.set_postfix({'Loss': f'{loss.item():.4f}'})
            
            avg_train_loss = train_loss / len(train_loader)
            train_losses.append(avg_train_loss)
            
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
                    
                    _, predicted = torch.max(outputs.data, 1)
                    total += batch_labels.size(0)
                    correct += (predicted == batch_labels).sum().item()
            
            avg_val_loss = val_loss / len(val_loader)
            val_accuracy = 100.0 * correct / total
            
            val_losses.append(avg_val_loss)
            val_accuracies.append(val_accuracy)
            
            # Update learning rate
            scheduler.step(avg_val_loss)
            
            logger.info(f'Epoch {epoch+1}: Train Loss={avg_train_loss:.4f}, Val Loss={avg_val_loss:.4f}, Val Acc={val_accuracy:.2f}%')
            
            # Early stopping
            if val_accuracy > best_val_acc:
                best_val_acc = val_accuracy
                patience_counter = 0
                # Save best model
                torch.save({
                    'model_state_dict': model.state_dict(),
                    'optimizer_state_dict': optimizer.state_dict(),
                    'val_accuracy': val_accuracy,
                    'epoch': epoch,
                    'feature_extractor': self.feature_extractor,
                    'label_encoder': self.label_encoder
                }, 'best_model.pth')
            else:
                patience_counter += 1
                
            if patience_counter >= patience:
                logger.info(f'Early stopping triggered after {epoch+1} epochs')
                break
        
        # Plot training curves
        self._plot_training_curves(train_losses, val_losses, val_accuracies)
        
        # Load best model (if it was saved)
        if os.path.exists('best_model.pth'):
            try:
                checkpoint = torch.load('best_model.pth', weights_only=False)
                model.load_state_dict(checkpoint['model_state_dict'])
                logger.info("Loaded best model from checkpoint")
            except Exception as e:
                logger.warning(f"Failed to load checkpoint: {e}. Using final model state.")
        else:
            logger.warning("No best model saved - using final model state")
        
        logger.info(f'Training completed. Best validation accuracy: {best_val_acc:.2f}%')
        return model
    
    def evaluate_model(self, model: nn.Module, test_loader: DataLoader) -> Dict[str, Any]:
        """Evaluate model performance on test set"""
        logger.info("Evaluating model on test set...")
        
        model.eval()
        y_true = []
        y_pred = []
        y_proba = []
        
        with torch.no_grad():
            for batch_features, batch_labels in test_loader:
                batch_features = batch_features.to(self.device)
                batch_labels = batch_labels.to(self.device)
                
                outputs = model(batch_features)
                probabilities = F.softmax(outputs, dim=1)
                _, predicted = torch.max(outputs, 1)
                
                y_true.extend(batch_labels.cpu().numpy())
                y_pred.extend(predicted.cpu().numpy())
                y_proba.extend(probabilities.cpu().numpy())
        
        # Calculate metrics
        accuracy = accuracy_score(y_true, y_pred)
        class_names = self.label_encoder.classes_
        
        # Classification report
        report = classification_report(y_true, y_pred, target_names=class_names, output_dict=True)
        
        # Confusion matrix
        cm = confusion_matrix(y_true, y_pred)
        
        # Plot confusion matrix
        self._plot_confusion_matrix(cm, class_names)
        
        results = {
            'accuracy': accuracy,
            'classification_report': report,
            'confusion_matrix': cm.tolist(),
            'class_names': class_names.tolist()
        }
        
        logger.info(f"Test Accuracy: {accuracy:.4f}")
        logger.info("\nClassification Report:")
        print(classification_report(y_true, y_pred, target_names=class_names))
        
        return results
    
    def _plot_training_curves(self, train_losses: List[float], val_losses: List[float], 
                            val_accuracies: List[float]):
        """Plot training and validation curves"""
        plt.figure(figsize=(15, 5))
        
        # Loss curves
        plt.subplot(1, 2, 1)
        plt.plot(train_losses, label='Training Loss')
        plt.plot(val_losses, label='Validation Loss')
        plt.title('Training and Validation Loss')
        plt.xlabel('Epoch')
        plt.ylabel('Loss')
        plt.legend()
        plt.grid(True)
        
        # Accuracy curve
        plt.subplot(1, 2, 2)
        plt.plot(val_accuracies, label='Validation Accuracy', color='orange')
        plt.title('Validation Accuracy')
        plt.xlabel('Epoch')
        plt.ylabel('Accuracy (%)')
        plt.legend()
        plt.grid(True)
        
        plt.tight_layout()
        plt.savefig('training_curves.png', dpi=300, bbox_inches='tight')
        plt.close()
        
        logger.info("Training curves saved to training_curves.png")
    
    def _plot_confusion_matrix(self, cm: np.ndarray, class_names: List[str]):
        """Plot confusion matrix"""
        plt.figure(figsize=(8, 6))
        sns.heatmap(cm, annot=True, fmt='d', cmap='Blues', 
                   xticklabels=class_names, yticklabels=class_names)
        plt.title('Confusion Matrix')
        plt.xlabel('Predicted')
        plt.ylabel('Actual')
        plt.tight_layout()
        plt.savefig('confusion_matrix.png', dpi=300, bbox_inches='tight')
        plt.close()
        
        logger.info("Confusion matrix saved to confusion_matrix.png")

def load_album_data(file_path: str) -> List[Dict[str, Any]]:
    """Load album data from JSON file"""
    with open(file_path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    return data.get('albums', [])

def save_results(results: Dict[str, Any], model_path: str):
    """Save training results and model metadata"""
    results_path = model_path.replace('.pth', '_results.json')
    
    with open(results_path, 'w') as f:
        json.dump(results, f, indent=2, default=str)
    
    logger.info(f"Results saved to {results_path}")

def main():
    """Main training function"""
    if not ML_AVAILABLE:
        logger.error("ML dependencies not available. Please install required packages.")
        sys.exit(1)
    
    parser = argparse.ArgumentParser(description="Train ML model for query complexity classification")
    parser.add_argument('--input', required=True, help='Input JSON file with album data')
    parser.add_argument('--output', default='trained_model.pth', help='Output model file')
    parser.add_argument('--gpu', action='store_true', help='Use GPU acceleration')
    parser.add_argument('--cpu', action='store_true', help='Force CPU usage')
    parser.add_argument('--batch-size', type=int, default=256, help='Batch size')
    parser.add_argument('--learning-rate', type=float, default=0.001, help='Learning rate')
    parser.add_argument('--epochs', type=int, default=100, help='Number of epochs')
    parser.add_argument('--hidden-sizes', nargs='+', type=int, default=[128, 64, 32], 
                       help='Hidden layer sizes')
    
    args = parser.parse_args()
    
    # Determine device
    if args.cpu:
        device = 'cpu'
    elif args.gpu and torch.cuda.is_available():
        device = 'cuda'
    else:
        device = 'cpu'
        if args.gpu and not torch.cuda.is_available():
            logger.warning("CUDA not available, falling back to CPU")
    
    # Load data
    logger.info(f"Loading data from {args.input}...")
    albums = load_album_data(args.input)
    logger.info(f"Loaded {len(albums)} albums")
    
    if len(albums) < 10:
        logger.error("Not enough training data. Need at least 10 albums for testing.")
        sys.exit(1)
    
    # Initialize trainer
    trainer = ModelTrainer(
        device=device,
        batch_size=args.batch_size,
        learning_rate=args.learning_rate,
        num_epochs=args.epochs
    )
    
    # Prepare data
    train_loader, val_loader, test_loader = trainer.prepare_data(albums)
    
    # Train model
    model = trainer.train_model(train_loader, val_loader)
    
    # Evaluate model
    results = trainer.evaluate_model(model, test_loader)
    
    # Save final model and results
    torch.save({
        'model_state_dict': model.state_dict(),
        'model_config': {
            'input_size': 25,
            'hidden_sizes': args.hidden_sizes,
            'num_classes': 3,
            'dropout_rate': 0.3
        },
        'feature_extractor': trainer.feature_extractor,
        'label_encoder': trainer.label_encoder,
        'results': results,
        'training_args': vars(args)
    }, args.output)
    
    save_results(results, args.output)
    
    logger.info("Training completed successfully!")
    logger.info(f"Model saved to: {args.output}")
    logger.info(f"Final test accuracy: {results['accuracy']:.4f}")

if __name__ == '__main__':
    main()