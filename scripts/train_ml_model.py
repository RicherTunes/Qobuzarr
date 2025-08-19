#!/usr/bin/env python3
"""
GPU-Accelerated ML Model Training for Qobuzarr

Trains neural network models using PyTorch with CUDA acceleration (RTX 3090)
to improve query complexity classification for better API call reduction.

Usage:
    python train_ml_model.py --input musicbrainz_albums.json --gpu --epochs 100
    python train_ml_model.py --input albums.json --cpu --batch-size 512 --lr 0.001
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
from sklearn.model_selection import train_test_split, StratifiedKFold
from sklearn.preprocessing import StandardScaler, LabelEncoder
from sklearn.metrics import classification_report, confusion_matrix, accuracy_score
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, WeightedRandomSampler
import torch.nn.functional as F
from tqdm import tqdm
import matplotlib.pyplot as plt
import seaborn as sns

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('ml_training.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

class QueryComplexityDataset(Dataset):
    """PyTorch dataset for query complexity classification"""
    
    def __init__(self, features: np.ndarray, labels: np.ndarray):
        self.features = torch.FloatTensor(features)
        self.labels = torch.LongTensor(labels)
    
    def __len__(self):
        return len(self.features)
    
    def __getitem__(self, idx):
        return self.features[idx], self.labels[idx]

class QueryComplexityNet(nn.Module):
    """Neural network for query complexity classification"""
    
    def __init__(self, input_size: int = 25, hidden_sizes: List[int] = [128, 64, 32], 
                 num_classes: int = 3, dropout_rate: float = 0.3):
        super(QueryComplexityNet, self).__init__()
        
        layers = []
        prev_size = input_size
        
        # Input layer with batch normalization
        layers.append(nn.Linear(prev_size, hidden_sizes[0]))
        layers.append(nn.BatchNorm1d(hidden_sizes[0]))
        layers.append(nn.ReLU())
        layers.append(nn.Dropout(dropout_rate))
        
        # Hidden layers
        for hidden_size in hidden_sizes[1:]:
            layers.append(nn.Linear(prev_size, hidden_size))
            layers.append(nn.BatchNorm1d(hidden_size))
            layers.append(nn.ReLU())
            layers.append(nn.Dropout(dropout_rate))
            prev_size = hidden_size
            
        # Output layer
        layers.append(nn.Linear(prev_size, num_classes))
        
        self.network = nn.Sequential(*layers)
        
        # Initialize weights
        self._initialize_weights()
    
    def _initialize_weights(self):
        """Initialize network weights using Xavier initialization"""
        for module in self.modules():
            if isinstance(module, nn.Linear):
                nn.init.xavier_uniform_(module.weight)
                nn.init.zeros_(module.bias)
    
    def forward(self, x):
        return self.network(x)

class FeatureExtractor:
    """Extract features from album data for ML training"""
    
    def __init__(self):
        self.scaler = StandardScaler()
        self.fitted = False
    
    def extract_features(self, albums: List[Dict[str, Any]]) -> np.ndarray:
        """Extract feature vectors from album data"""
        features = []
        
        for album in albums:
            artist = album.get('artist_name', '').lower()
            title = album.get('album_title', '').lower()
            
            feature_vector = [
                # Length features
                len(artist),
                len(title),
                len(artist.split()),
                len(title.split()),
                
                # Character analysis
                self._count_special_chars(artist),
                self._count_special_chars(title),
                self._count_numbers(artist),
                self._count_numbers(title),
                
                # Pattern detection
                int(self._has_pattern(title, 'remaster')),
                int(self._has_pattern(title, 'deluxe')),
                int(self._has_pattern(title, 'edition')),
                int(self._has_pattern(title, 'live')),
                int(self._has_pattern(title, 'greatest hits')),
                int(self._has_pattern(artist, 'various')),
                int(self._has_pattern(title, 'soundtrack')),
                int(self._has_pattern(title, 'vol')),
                int(self._has_pattern(title, 'part')),
                int(self._has_pattern(artist, 'feat')),
                int(self._has_pattern(title, 'anniversary')),
                
                # Unicode and punctuation
                int(self._has_non_ascii(artist)),
                int(self._has_non_ascii(title)),
                self._count_punctuation(artist),
                self._count_punctuation(title),
                
                # Length indicators
                int(len(artist) > 50),
                int(len(title) > 50),
            ]
            
            features.append(feature_vector)
        
        features_array = np.array(features, dtype=np.float32)
        
        if not self.fitted:
            features_array = self.scaler.fit_transform(features_array)
            self.fitted = True
        else:
            features_array = self.scaler.transform(features_array)
            
        return features_array
    
    def _count_special_chars(self, text: str) -> int:
        return sum(1 for c in text if c in "[&+/\\-:'\"()]")
    
    def _count_numbers(self, text: str) -> int:
        return sum(1 for c in text if c.isdigit())
    
    def _count_punctuation(self, text: str) -> int:
        import string
        return sum(1 for c in text if c in string.punctuation)
    
    def _has_pattern(self, text: str, pattern: str) -> bool:
        return pattern.lower() in text.lower()
    
    def _has_non_ascii(self, text: str) -> bool:
        return any(ord(c) > 127 for c in text)

class ComplexityClassifier:
    """Rule-based classifier for generating training labels"""
    
    def classify_complexity(self, artist: str, album: str) -> str:
        """Classify query complexity using rule-based logic"""
        artist = artist.lower()
        album = album.lower()
        
        # Complex indicators
        complex_patterns = [
            'various artists', 'va', 'soundtrack', 'compilation',
            'greatest hits', 'best of', 'live at', 'unplugged',
            'deluxe', 'anniversary', 'special edition', 'collector',
            'expanded', 'remaster', 'remix'
        ]
        
        # Check for complex patterns
        if any(pattern in artist or pattern in album for pattern in complex_patterns):
            return 'Complex'
        
        # Simple indicators
        if (len(artist.split()) <= 2 and len(album.split()) <= 3 and 
            len(album) < 30 and '(' not in album and '[' not in album):
            return 'Simple'
        
        # Default to Medium
        return 'Medium'

class ModelTrainer:
    """Main training class with GPU acceleration"""
    
    def __init__(self, device: str = 'cuda', batch_size: int = 256, 
                 learning_rate: float = 0.001, num_epochs: int = 100):
        self.device = torch.device(device if torch.cuda.is_available() else 'cpu')
        self.batch_size = batch_size
        self.learning_rate = learning_rate
        self.num_epochs = num_epochs
        
        logger.info(f"Using device: {self.device}")
        if self.device.type == 'cuda':
            logger.info(f"GPU: {torch.cuda.get_device_name()}")
            logger.info(f"CUDA Memory: {torch.cuda.get_device_properties(0).total_memory / 1e9:.1f} GB")
    
    def prepare_data(self, albums: List[Dict[str, Any]]) -> Tuple[DataLoader, DataLoader, DataLoader]:
        """Prepare data loaders for training, validation, and testing"""
        logger.info("Preparing training data...")
        
        # Extract features and labels
        feature_extractor = FeatureExtractor()
        features = feature_extractor.extract_features(albums)
        
        # Generate labels using rule-based classifier
        classifier = ComplexityClassifier()
        labels = [classifier.classify_complexity(
            album.get('artist_name', ''), 
            album.get('album_title', '')
        ) for album in albums]
        
        # Encode labels
        label_encoder = LabelEncoder()
        encoded_labels = label_encoder.fit_transform(labels)
        
        # Log class distribution
        unique, counts = np.unique(encoded_labels, return_counts=True)
        class_distribution = dict(zip(label_encoder.classes_, counts))
        logger.info(f"Class distribution: {class_distribution}")
        
        # Split data
        X_temp, X_test, y_temp, y_test = train_test_split(
            features, encoded_labels, test_size=0.2, random_state=42, stratify=encoded_labels
        )
        X_train, X_val, y_train, y_val = train_test_split(
            X_temp, y_temp, test_size=0.25, random_state=42, stratify=y_temp
        )
        
        logger.info(f"Training samples: {len(X_train)}")
        logger.info(f"Validation samples: {len(X_val)}")
        logger.info(f"Test samples: {len(X_test)}")
        
        # Create datasets
        train_dataset = QueryComplexityDataset(X_train, y_train)
        val_dataset = QueryComplexityDataset(X_val, y_val)
        test_dataset = QueryComplexityDataset(X_test, y_test)
        
        # Create weighted sampler for imbalanced classes
        class_weights = torch.FloatTensor([1.0 / counts[i] for i in range(len(counts))])
        sample_weights = class_weights[y_train]
        sampler = WeightedRandomSampler(sample_weights, len(sample_weights))
        
        # Create data loaders
        train_loader = DataLoader(train_dataset, batch_size=self.batch_size, sampler=sampler)
        val_loader = DataLoader(val_dataset, batch_size=self.batch_size, shuffle=False)
        test_loader = DataLoader(test_dataset, batch_size=self.batch_size, shuffle=False)
        
        # Store for later use
        self.feature_extractor = feature_extractor
        self.label_encoder = label_encoder
        self.class_weights = class_weights.to(self.device)
        
        return train_loader, val_loader, test_loader
    
    def train_model(self, train_loader: DataLoader, val_loader: DataLoader) -> nn.Module:
        """Train the neural network model"""
        logger.info("Starting model training...")
        
        # Initialize model
        model = QueryComplexityNet(
            input_size=25,
            hidden_sizes=[128, 64, 32],
            num_classes=3,
            dropout_rate=0.3
        ).to(self.device)
        
        # Loss function with class weights
        criterion = nn.CrossEntropyLoss(weight=self.class_weights)
        
        # Optimizer with weight decay
        optimizer = optim.AdamW(model.parameters(), lr=self.learning_rate, weight_decay=1e-4)
        
        # Learning rate scheduler
        scheduler = optim.lr_scheduler.ReduceLROnPlateau(
            optimizer, mode='min', factor=0.5, patience=10, verbose=True
        )
        
        # Training loop
        train_losses = []
        val_losses = []
        val_accuracies = []
        best_val_acc = 0.0
        patience_counter = 0
        patience = 20
        
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
                
                # Gradient clipping
                torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)
                
                optimizer.step()
                
                train_loss += loss.item()
                train_progress.set_postfix({'Loss': f'{loss.item():.4f}'})\n            \n            avg_train_loss = train_loss / len(train_loader)\n            train_losses.append(avg_train_loss)\n            \n            # Validation phase\n            model.eval()\n            val_loss = 0.0\n            correct = 0\n            total = 0\n            \n            with torch.no_grad():\n                for batch_features, batch_labels in val_loader:\n                    batch_features = batch_features.to(self.device)\n                    batch_labels = batch_labels.to(self.device)\n                    \n                    outputs = model(batch_features)\n                    loss = criterion(outputs, batch_labels)\n                    val_loss += loss.item()\n                    \n                    _, predicted = torch.max(outputs.data, 1)\n                    total += batch_labels.size(0)\n                    correct += (predicted == batch_labels).sum().item()\n            \n            avg_val_loss = val_loss / len(val_loader)\n            val_accuracy = 100.0 * correct / total\n            \n            val_losses.append(avg_val_loss)\n            val_accuracies.append(val_accuracy)\n            \n            # Update learning rate\n            scheduler.step(avg_val_loss)\n            \n            logger.info(f'Epoch {epoch+1}: Train Loss={avg_train_loss:.4f}, Val Loss={avg_val_loss:.4f}, Val Acc={val_accuracy:.2f}%')\n            \n            # Early stopping\n            if val_accuracy > best_val_acc:\n                best_val_acc = val_accuracy\n                patience_counter = 0\n                # Save best model\n                torch.save({\n                    'model_state_dict': model.state_dict(),\n                    'optimizer_state_dict': optimizer.state_dict(),\n                    'val_accuracy': val_accuracy,\n                    'epoch': epoch,\n                    'feature_extractor': self.feature_extractor,\n                    'label_encoder': self.label_encoder\n                }, 'best_model.pth')\n            else:\n                patience_counter += 1\n                \n            if patience_counter >= patience:\n                logger.info(f'Early stopping triggered after {epoch+1} epochs')\n                break\n        \n        # Plot training curves\n        self._plot_training_curves(train_losses, val_losses, val_accuracies)\n        \n        # Load best model\n        checkpoint = torch.load('best_model.pth')\n        model.load_state_dict(checkpoint['model_state_dict'])\n        \n        logger.info(f'Training completed. Best validation accuracy: {best_val_acc:.2f}%')\n        return model\n    \n    def evaluate_model(self, model: nn.Module, test_loader: DataLoader) -> Dict[str, Any]:\n        \"\"\"Evaluate model performance on test set\"\"\"\n        logger.info(\"Evaluating model on test set...\")\n        \n        model.eval()\n        y_true = []\n        y_pred = []\n        y_proba = []\n        \n        with torch.no_grad():\n            for batch_features, batch_labels in test_loader:\n                batch_features = batch_features.to(self.device)\n                batch_labels = batch_labels.to(self.device)\n                \n                outputs = model(batch_features)\n                probabilities = F.softmax(outputs, dim=1)\n                _, predicted = torch.max(outputs, 1)\n                \n                y_true.extend(batch_labels.cpu().numpy())\n                y_pred.extend(predicted.cpu().numpy())\n                y_proba.extend(probabilities.cpu().numpy())\n        \n        # Calculate metrics\n        accuracy = accuracy_score(y_true, y_pred)\n        class_names = self.label_encoder.classes_\n        \n        # Classification report\n        report = classification_report(y_true, y_pred, target_names=class_names, output_dict=True)\n        \n        # Confusion matrix\n        cm = confusion_matrix(y_true, y_pred)\n        \n        # Plot confusion matrix\n        self._plot_confusion_matrix(cm, class_names)\n        \n        results = {\n            'accuracy': accuracy,\n            'classification_report': report,\n            'confusion_matrix': cm.tolist(),\n            'class_names': class_names.tolist()\n        }\n        \n        logger.info(f\"Test Accuracy: {accuracy:.4f}\")\n        logger.info(\"\\nClassification Report:\")\n        print(classification_report(y_true, y_pred, target_names=class_names))\n        \n        return results\n    \n    def _plot_training_curves(self, train_losses: List[float], val_losses: List[float], \n                            val_accuracies: List[float]):\n        \"\"\"Plot training and validation curves\"\"\"\n        plt.figure(figsize=(15, 5))\n        \n        # Loss curves\n        plt.subplot(1, 2, 1)\n        plt.plot(train_losses, label='Training Loss')\n        plt.plot(val_losses, label='Validation Loss')\n        plt.title('Training and Validation Loss')\n        plt.xlabel('Epoch')\n        plt.ylabel('Loss')\n        plt.legend()\n        plt.grid(True)\n        \n        # Accuracy curve\n        plt.subplot(1, 2, 2)\n        plt.plot(val_accuracies, label='Validation Accuracy', color='orange')\n        plt.title('Validation Accuracy')\n        plt.xlabel('Epoch')\n        plt.ylabel('Accuracy (%)')\n        plt.legend()\n        plt.grid(True)\n        \n        plt.tight_layout()\n        plt.savefig('training_curves.png', dpi=300, bbox_inches='tight')\n        plt.close()\n        \n        logger.info(\"Training curves saved to training_curves.png\")\n    \n    def _plot_confusion_matrix(self, cm: np.ndarray, class_names: List[str]):\n        \"\"\"Plot confusion matrix\"\"\"\n        plt.figure(figsize=(8, 6))\n        sns.heatmap(cm, annot=True, fmt='d', cmap='Blues', \n                   xticklabels=class_names, yticklabels=class_names)\n        plt.title('Confusion Matrix')\n        plt.xlabel('Predicted')\n        plt.ylabel('Actual')\n        plt.tight_layout()\n        plt.savefig('confusion_matrix.png', dpi=300, bbox_inches='tight')\n        plt.close()\n        \n        logger.info(\"Confusion matrix saved to confusion_matrix.png\")\n\ndef load_album_data(file_path: str) -> List[Dict[str, Any]]:\n    \"\"\"Load album data from JSON file\"\"\"\n    with open(file_path, 'r', encoding='utf-8') as f:\n        data = json.load(f)\n    return data.get('albums', [])\n\ndef save_results(results: Dict[str, Any], model_path: str):\n    \"\"\"Save training results and model metadata\"\"\"\n    results_path = model_path.replace('.pth', '_results.json')\n    \n    with open(results_path, 'w') as f:\n        json.dump(results, f, indent=2)\n    \n    logger.info(f\"Results saved to {results_path}\")\n\ndef main():\n    parser = argparse.ArgumentParser(description=\"Train ML model for query complexity classification\")\n    parser.add_argument('--input', required=True, help='Input JSON file with album data')\n    parser.add_argument('--output', default='trained_model.pth', help='Output model file')\n    parser.add_argument('--gpu', action='store_true', help='Use GPU acceleration')\n    parser.add_argument('--cpu', action='store_true', help='Force CPU usage')\n    parser.add_argument('--batch-size', type=int, default=256, help='Batch size')\n    parser.add_argument('--learning-rate', type=float, default=0.001, help='Learning rate')\n    parser.add_argument('--epochs', type=int, default=100, help='Number of epochs')\n    parser.add_argument('--hidden-sizes', nargs='+', type=int, default=[128, 64, 32], \n                       help='Hidden layer sizes')\n    \n    args = parser.parse_args()\n    \n    # Determine device\n    if args.cpu:\n        device = 'cpu'\n    elif args.gpu and torch.cuda.is_available():\n        device = 'cuda'\n    else:\n        device = 'cpu'\n        if args.gpu and not torch.cuda.is_available():\n            logger.warning(\"CUDA not available, falling back to CPU\")\n    \n    # Load data\n    logger.info(f\"Loading data from {args.input}...\")\n    albums = load_album_data(args.input)\n    logger.info(f\"Loaded {len(albums)} albums\")\n    \n    if len(albums) < 100:\n        logger.error(\"Not enough training data. Need at least 100 albums.\")\n        sys.exit(1)\n    \n    # Initialize trainer\n    trainer = ModelTrainer(\n        device=device,\n        batch_size=args.batch_size,\n        learning_rate=args.learning_rate,\n        num_epochs=args.epochs\n    )\n    \n    # Prepare data\n    train_loader, val_loader, test_loader = trainer.prepare_data(albums)\n    \n    # Train model\n    model = trainer.train_model(train_loader, val_loader)\n    \n    # Evaluate model\n    results = trainer.evaluate_model(model, test_loader)\n    \n    # Save final model and results\n    torch.save({\n        'model_state_dict': model.state_dict(),\n        'model_config': {\n            'input_size': 25,\n            'hidden_sizes': args.hidden_sizes,\n            'num_classes': 3,\n            'dropout_rate': 0.3\n        },\n        'feature_extractor': trainer.feature_extractor,\n        'label_encoder': trainer.label_encoder,\n        'results': results,\n        'training_args': vars(args)\n    }, args.output)\n    \n    save_results(results, args.output)\n    \n    logger.info(f\"✅ Training completed successfully!\")\n    logger.info(f\"Model saved to: {args.output}\")\n    logger.info(f\"Final test accuracy: {results['accuracy']:.4f}\")\n\nif __name__ == '__main__':\n    main()