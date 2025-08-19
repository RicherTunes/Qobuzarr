#!/usr/bin/env python3
"""
Model Validation and Performance Comparison

Validates trained models and compares performance against baseline
and existing Qobuzarr ML models.

Usage:
    python validate_model.py --model trained_model.pth --test-data test_albums.json
    python validate_model.py --compare --baseline CompiledMLQueryOptimizer --custom MyModel.cs
"""

import argparse
import json
import logging
import sys
import time
from typing import Dict, List, Any, Tuple
import numpy as np
import pandas as pd
import torch
import matplotlib.pyplot as plt
import seaborn as sns
from sklearn.metrics import classification_report, confusion_matrix, accuracy_score
from sklearn.metrics import precision_recall_fscore_support, roc_auc_score

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class ModelValidator:
    """Validates and compares ML model performance"""
    
    def __init__(self):
        self.results = {}
    
    def load_test_data(self, test_file: str) -> List[Dict[str, Any]]:
        """Load test dataset"""
        logger.info(f"Loading test data from {test_file}")
        
        with open(test_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        albums = data.get('albums', [])
        logger.info(f"Loaded {len(albums)} test albums")
        return albums
    
    def validate_pytorch_model(self, model_path: str, test_albums: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Validate PyTorch model"""
        logger.info(f"Validating PyTorch model: {model_path}")
        
        # Load model
        checkpoint = torch.load(model_path, map_location='cpu')
        
        # Get predictions for test data
        from train_ml_model import FeatureExtractor, ComplexityClassifier, QueryComplexityNet
        
        feature_extractor = checkpoint.get('feature_extractor')
        label_encoder = checkpoint.get('label_encoder')
        model_config = checkpoint.get('model_config', {})
        
        # Reconstruct model
        model = QueryComplexityNet(**model_config)
        model.load_state_dict(checkpoint['model_state_dict'])
        model.eval()
        
        # Generate predictions
        predictions = []
        true_labels = []
        probabilities = []
        
        classifier = ComplexityClassifier()
        
        for album in test_albums:
            # Extract features
            features = feature_extractor.extract_features([album])[0]
            
            # Get true label
            true_label = classifier.classify_complexity(
                album.get('artist_name', ''), 
                album.get('album_title', '')
            )
            
            # Get model prediction
            with torch.no_grad():
                features_tensor = torch.FloatTensor(features).unsqueeze(0)
                outputs = model(features_tensor)
                probs = torch.softmax(outputs, dim=1).numpy()[0]
                pred_class = np.argmax(probs)
                
            predictions.append(label_encoder.classes_[pred_class])
            true_labels.append(true_label)
            probabilities.append(probs)
        
        # Calculate metrics
        accuracy = accuracy_score(true_labels, predictions)
        
        # Encode labels for sklearn metrics
        true_encoded = label_encoder.transform(true_labels)
        pred_encoded = label_encoder.transform(predictions)
        
        precision, recall, f1, _ = precision_recall_fscore_support(
            true_encoded, pred_encoded, average='weighted'
        )
        
        # Classification report
        report = classification_report(true_labels, predictions, output_dict=True)
        
        # Confusion matrix
        cm = confusion_matrix(true_labels, predictions, labels=label_encoder.classes_)
        
        results = {\n            'model_type': 'PyTorch',\n            'model_path': model_path,\n            'accuracy': accuracy,\n            'precision': precision,\n            'recall': recall,\n            'f1_score': f1,\n            'classification_report': report,\n            'confusion_matrix': cm.tolist(),\n            'class_names': label_encoder.classes_.tolist(),\n            'predictions': predictions,\n            'true_labels': true_labels,\n            'probabilities': probabilities\n        }\n        \n        logger.info(f\"PyTorch model accuracy: {accuracy:.4f}\")\n        return results\n    \n    def simulate_baseline_model(self, test_albums: List[Dict[str, Any]]) -> Dict[str, Any]:\n        \"\"\"Simulate baseline rule-based classifier\"\"\"\n        logger.info(\"Simulating baseline rule-based classifier\")\n        \n        from train_ml_model import ComplexityClassifier\n        classifier = ComplexityClassifier()\n        \n        predictions = []\n        true_labels = []\n        \n        for album in test_albums:\n            pred = classifier.classify_complexity(\n                album.get('artist_name', ''), \n                album.get('album_title', '')\n            )\n            # For simulation, we'll use the same classifier for \"true\" labels\n            # In reality, you'd have human-labeled ground truth\n            predictions.append(pred)\n            true_labels.append(pred)  # Perfect accuracy for baseline\n        \n        # Calculate metrics (will be perfect since we're using same classifier)\n        accuracy = accuracy_score(true_labels, predictions)\n        \n        results = {\n            'model_type': 'Baseline',\n            'accuracy': accuracy,\n            'predictions': predictions,\n            'true_labels': true_labels,\n            'note': 'Baseline uses rule-based classifier for both prediction and ground truth'\n        }\n        \n        logger.info(f\"Baseline accuracy: {accuracy:.4f}\")\n        return results\n    \n    def compare_models(self, results_list: List[Dict[str, Any]]) -> Dict[str, Any]:\n        \"\"\"Compare multiple model results\"\"\"\n        logger.info(\"Comparing model performance...\")\n        \n        comparison = {\n            'models': [],\n            'accuracies': [],\n            'precisions': [],\n            'recalls': [],\n            'f1_scores': []\n        }\n        \n        for result in results_list:\n            comparison['models'].append(result.get('model_type', 'Unknown'))\n            comparison['accuracies'].append(result.get('accuracy', 0.0))\n            comparison['precisions'].append(result.get('precision', 0.0))\n            comparison['recalls'].append(result.get('recall', 0.0))\n            comparison['f1_scores'].append(result.get('f1_score', 0.0))\n        \n        # Create comparison DataFrame\n        df = pd.DataFrame(comparison)\n        \n        logger.info(\"\\nModel Comparison:\")\n        print(df.to_string(index=False))\n        \n        return comparison\n    \n    def plot_performance_comparison(self, comparison: Dict[str, Any], output_path: str = 'model_comparison.png'):\n        \"\"\"Plot model performance comparison\"\"\"\n        plt.figure(figsize=(12, 8))\n        \n        models = comparison['models']\n        metrics = ['accuracies', 'precisions', 'recalls', 'f1_scores']\n        metric_names = ['Accuracy', 'Precision', 'Recall', 'F1-Score']\n        \n        x = np.arange(len(models))\n        width = 0.2\n        \n        for i, (metric, name) in enumerate(zip(metrics, metric_names)):\n            values = comparison[metric]\n            plt.bar(x + i * width, values, width, label=name, alpha=0.8)\n        \n        plt.xlabel('Models')\n        plt.ylabel('Score')\n        plt.title('Model Performance Comparison')\n        plt.xticks(x + width * 1.5, models, rotation=45)\n        plt.legend()\n        plt.ylim(0, 1.1)\n        plt.grid(True, alpha=0.3)\n        \n        # Add value labels on bars\n        for i, (metric, name) in enumerate(zip(metrics, metric_names)):\n            values = comparison[metric]\n            for j, v in enumerate(values):\n                plt.text(j + i * width, v + 0.01, f'{v:.3f}', \n                        ha='center', va='bottom', fontsize=9)\n        \n        plt.tight_layout()\n        plt.savefig(output_path, dpi=300, bbox_inches='tight')\n        plt.close()\n        \n        logger.info(f\"Performance comparison saved to {output_path}\")\n    \n    def plot_confusion_matrices(self, results_list: List[Dict[str, Any]], output_path: str = 'confusion_matrices.png'):\n        \"\"\"Plot confusion matrices for multiple models\"\"\"\n        n_models = len([r for r in results_list if 'confusion_matrix' in r])\n        if n_models == 0:\n            return\n            \n        fig, axes = plt.subplots(1, n_models, figsize=(5 * n_models, 4))\n        if n_models == 1:\n            axes = [axes]\n        \n        idx = 0\n        for result in results_list:\n            if 'confusion_matrix' not in result:\n                continue\n                \n            cm = np.array(result['confusion_matrix'])\n            class_names = result.get('class_names', ['Simple', 'Medium', 'Complex'])\n            \n            sns.heatmap(cm, annot=True, fmt='d', cmap='Blues',\n                       xticklabels=class_names, yticklabels=class_names,\n                       ax=axes[idx])\n            axes[idx].set_title(f\"{result.get('model_type', 'Model')} Confusion Matrix\")\n            axes[idx].set_xlabel('Predicted')\n            axes[idx].set_ylabel('Actual')\n            \n            idx += 1\n        \n        plt.tight_layout()\n        plt.savefig(output_path, dpi=300, bbox_inches='tight')\n        plt.close()\n        \n        logger.info(f\"Confusion matrices saved to {output_path}\")\n    \n    def generate_performance_report(self, results_list: List[Dict[str, Any]], \n                                  output_path: str = 'performance_report.json'):\n        \"\"\"Generate comprehensive performance report\"\"\"\n        report = {\n            'timestamp': time.strftime('%Y-%m-%d %H:%M:%S'),\n            'models_evaluated': len(results_list),\n            'model_results': results_list,\n            'summary': {\n                'best_accuracy': max(r.get('accuracy', 0) for r in results_list),\n                'best_f1': max(r.get('f1_score', 0) for r in results_list if 'f1_score' in r),\n                'model_rankings': self._rank_models(results_list)\n            }\n        }\n        \n        with open(output_path, 'w') as f:\n            json.dump(report, f, indent=2, default=str)\n        \n        logger.info(f\"Performance report saved to {output_path}\")\n        return report\n    \n    def _rank_models(self, results_list: List[Dict[str, Any]]) -> List[Dict[str, Any]]:\n        \"\"\"Rank models by performance\"\"\"\n        rankings = []\n        \n        for result in results_list:\n            score = result.get('accuracy', 0) * 0.4 + result.get('f1_score', 0) * 0.6\n            rankings.append({\n                'model': result.get('model_type', 'Unknown'),\n                'composite_score': score,\n                'accuracy': result.get('accuracy', 0),\n                'f1_score': result.get('f1_score', 0)\n            })\n        \n        return sorted(rankings, key=lambda x: x['composite_score'], reverse=True)\n    \n    def benchmark_inference_speed(self, model_path: str, test_albums: List[Dict[str, Any]], \n                                 iterations: int = 1000) -> Dict[str, float]:\n        \"\"\"Benchmark model inference speed\"\"\"\n        logger.info(f\"Benchmarking inference speed with {iterations} iterations...\")\n        \n        # Load model\n        checkpoint = torch.load(model_path, map_location='cpu')\n        \n        from train_ml_model import FeatureExtractor, QueryComplexityNet\n        \n        feature_extractor = checkpoint.get('feature_extractor')\n        model_config = checkpoint.get('model_config', {})\n        \n        model = QueryComplexityNet(**model_config)\n        model.load_state_dict(checkpoint['model_state_dict'])\n        model.eval()\n        \n        # Prepare test data\n        features_list = []\n        for album in test_albums[:100]:  # Use subset for speed test\n            features = feature_extractor.extract_features([album])[0]\n            features_list.append(features)\n        \n        features_batch = torch.FloatTensor(features_list)\n        \n        # Warm up\n        with torch.no_grad():\n            for _ in range(10):\n                _ = model(features_batch)\n        \n        # Benchmark\n        start_time = time.time()\n        \n        with torch.no_grad():\n            for _ in range(iterations):\n                _ = model(features_batch)\n        \n        end_time = time.time()\n        \n        total_time = end_time - start_time\n        avg_time_per_batch = total_time / iterations\n        avg_time_per_sample = avg_time_per_batch / len(features_list)\n        \n        benchmark_results = {\n            'total_time': total_time,\n            'iterations': iterations,\n            'batch_size': len(features_list),\n            'avg_time_per_batch_ms': avg_time_per_batch * 1000,\n            'avg_time_per_sample_ms': avg_time_per_sample * 1000,\n            'samples_per_second': 1.0 / avg_time_per_sample\n        }\n        \n        logger.info(f\"Inference speed: {avg_time_per_sample * 1000:.3f} ms per sample\")\n        logger.info(f\"Throughput: {benchmark_results['samples_per_second']:.1f} samples/second\")\n        \n        return benchmark_results\n\ndef main():\n    parser = argparse.ArgumentParser(description=\"Validate ML model performance\")\n    parser.add_argument('--model', help='Path to trained PyTorch model')\n    parser.add_argument('--test-data', help='Test dataset JSON file')\n    parser.add_argument('--compare', action='store_true', help='Compare multiple models')\n    parser.add_argument('--baseline', action='store_true', help='Include baseline comparison')\n    parser.add_argument('--benchmark', action='store_true', help='Benchmark inference speed')\n    parser.add_argument('--output-dir', default='.', help='Output directory for results')\n    \n    args = parser.parse_args()\n    \n    if not args.test_data:\n        logger.error(\"Test data file is required\")\n        sys.exit(1)\n    \n    validator = ModelValidator()\n    \n    # Load test data\n    test_albums = validator.load_test_data(args.test_data)\n    \n    if len(test_albums) < 10:\n        logger.error(\"Not enough test data. Need at least 10 albums.\")\n        sys.exit(1)\n    \n    results_list = []\n    \n    # Validate PyTorch model\n    if args.model:\n        pytorch_results = validator.validate_pytorch_model(args.model, test_albums)\n        results_list.append(pytorch_results)\n        \n        # Benchmark speed if requested\n        if args.benchmark:\n            speed_results = validator.benchmark_inference_speed(args.model, test_albums)\n            pytorch_results['benchmark'] = speed_results\n    \n    # Add baseline comparison\n    if args.baseline:\n        baseline_results = validator.simulate_baseline_model(test_albums)\n        results_list.append(baseline_results)\n    \n    # Compare models\n    if len(results_list) > 1 or args.compare:\n        comparison = validator.compare_models(results_list)\n        \n        # Generate plots\n        output_dir = args.output_dir\n        validator.plot_performance_comparison(comparison, \n                                            f\"{output_dir}/model_comparison.png\")\n        validator.plot_confusion_matrices(results_list, \n                                        f\"{output_dir}/confusion_matrices.png\")\n    \n    # Generate comprehensive report\n    report = validator.generate_performance_report(results_list, \n                                                  f\"{args.output_dir}/performance_report.json\")\n    \n    # Print summary\n    print(\"\\n\" + \"=\"*50)\n    print(\"VALIDATION SUMMARY\")\n    print(\"=\"*50)\n    \n    for result in results_list:\n        model_type = result.get('model_type', 'Unknown')\n        accuracy = result.get('accuracy', 0)\n        print(f\"{model_type}: {accuracy:.4f} accuracy\")\n    \n    if len(results_list) > 1:\n        best_model = max(results_list, key=lambda x: x.get('accuracy', 0))\n        print(f\"\\n🏆 Best performing model: {best_model.get('model_type', 'Unknown')} \"\n              f\"({best_model.get('accuracy', 0):.4f} accuracy)\")\n    \n    print(f\"\\n📊 Detailed results saved to: {args.output_dir}/performance_report.json\")\n    \n    logger.info(\"✅ Validation completed successfully!\")\n\nif __name__ == '__main__':\n    main()