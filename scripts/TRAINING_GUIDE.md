# Complete ML Training Guide for Qobuzarr

This guide walks you through creating personalized ML models using your local MusicBrainz instance and RTX 3090 GPU.

## Table of Contents

1. [Quick Start (5 minutes)](#quick-start)
2. [Prerequisites](#prerequisites)
3. [Step-by-Step Training](#step-by-step-training)
4. [Advanced Configuration](#advanced-configuration)
5. [Integration with Qobuzarr](#integration)
6. [Performance Tuning](#performance-tuning)
7. [Troubleshooting](#troubleshooting)

## Quick Start

**For impatient users who want results fast:**

```bash
# 1. One-click environment setup
python setup_environment.py

# 2. Quick training with your MusicBrainz instance
python quick_start.py --mb-url http://192.168.2.13:5001/ --fast-mode

# 3. Follow the generated integration guide
cat training_output/integration_guide.md
```

This will extract 1,000 albums, train for 10 epochs (~5 minutes on RTX 3090), and generate a personalized C# model.

## Prerequisites

### Hardware Requirements
- **GPU**: NVIDIA RTX 3090 (or other CUDA-compatible GPU)
- **RAM**: 16GB+ recommended
- **Storage**: 5GB free space for datasets and models

### Software Requirements
- **Python**: 3.8 or newer
- **CUDA**: 11.8+ for GPU acceleration
- **MusicBrainz**: Local instance running at `http://192.168.2.13:5001/`

### Environment Setup

```bash
# Install Python dependencies
pip install -r requirements.txt

# Check GPU support
python setup_environment.py --gpu-check

# Install CUDA-enabled PyTorch (if needed)
python setup_environment.py --install-cuda

# Test environment
python test_environment.py
```

## Step-by-Step Training

### Step 1: Extract Training Data

Extract album data from your MusicBrainz instance:

```bash
# Basic extraction (50,000 albums)
python extract_musicbrainz_data.py \
    --mb-url http://192.168.2.13:5001/ \
    --output my_musicbrainz_data.json \
    --max-albums 50000

# With PostgreSQL direct access (faster)
python extract_musicbrainz_data.py \
    --mb-url http://192.168.2.13:5001/ \
    --mb-database-url "postgresql://user:pass@192.168.2.13:5432/musicbrainz" \
    --output my_musicbrainz_data.json \
    --max-albums 100000
```

**Expected output:**
- `my_musicbrainz_data.json` - Training dataset
- `my_musicbrainz_data.csv` - CSV for analysis
- Extraction logs with statistics

### Step 2: Train the Model

Train a neural network with GPU acceleration:

```bash
# Full training (recommended)
python train_ml_model.py \
    --input my_musicbrainz_data.json \
    --output my_trained_model.pth \
    --gpu \
    --epochs 100 \
    --batch-size 512

# Quick training (for testing)
python train_ml_model.py \
    --input my_musicbrainz_data.json \
    --output quick_model.pth \
    --gpu \
    --epochs 20 \
    --batch-size 256
```

**Training time estimates:**
- RTX 3090: ~15-30 minutes for 100 epochs
- RTX 4090: ~10-20 minutes for 100 epochs
- CPU only: 2-4 hours for 100 epochs

**Generated files:**
- `my_trained_model.pth` - Trained PyTorch model
- `best_model.pth` - Best model checkpoint
- `training_curves.png` - Training/validation plots
- `confusion_matrix.png` - Classification results

### Step 3: Export to C# Code

Convert the trained model to production C# code:

```bash
python export_model_to_csharp.py \
    --model my_trained_model.pth \
    --output MyPersonalizedMLOptimizer.cs \
    --class MyPersonalizedMLOptimizer \
    --namespace Lidarr.Plugin.Qobuzarr.Indexers
```

**Generated file:**
- `MyPersonalizedMLOptimizer.cs` - Pure C# implementation with no ML.NET dependency

### Step 4: Validate Performance

Compare your model against the baseline:

```bash
python validate_model.py \
    --model my_trained_model.pth \
    --test-data my_musicbrainz_data.json \
    --baseline \
    --benchmark \
    --output-dir validation_results
```

**Generated files:**
- `performance_report.json` - Detailed metrics
- `model_comparison.png` - Performance comparison chart
- `confusion_matrices.png` - Classification matrices

## Advanced Configuration

### Configuration File

Create `my_config.json` for advanced settings:

```json
{
  "musicbrainz": {
    "url": "http://192.168.2.13:5001/",
    "database_url": "postgresql://user:pass@192.168.2.13:5432/musicbrainz",
    "max_albums": 100000,
    "min_track_count": 3,
    "max_track_count": 50,
    "include_genres": ["rock", "pop", "electronic", "jazz", "classical"],
    "exclude_album_types": ["Compilation"]
  },
  "training": {
    "batch_size": 512,
    "learning_rate": 0.001,
    "num_epochs": 100,
    "hidden_sizes": [256, 128, 64],
    "dropout_rate": 0.3,
    "use_gpu": true,
    "early_stopping_patience": 20
  },
  "output": {
    "class_name": "MyCustomMLOptimizer",
    "namespace": "Lidarr.Plugin.Qobuzarr.Indexers"
  }
}
```

Use the configuration:

```bash
python quick_start.py --config my_config.json
```

### Feature Engineering

Customize feature extraction by modifying `train_ml_model.py`:

```python
# Add your own features in FeatureExtractor.extract_features()
def extract_features(self, albums):
    for album in albums:
        # Your custom features here
        has_remix = int('remix' in album['album_title'].lower())
        is_soundtrack = int('soundtrack' in album['album_title'].lower())
        # ... add to feature vector
```

### Hyperparameter Tuning

Find optimal hyperparameters:

```bash
# Grid search over learning rates
for lr in 0.001 0.0005 0.0001; do
    python train_ml_model.py \
        --input data.json \
        --output model_lr_$lr.pth \
        --learning-rate $lr \
        --epochs 50
done

# Compare results
python validate_model.py --compare \
    --model model_lr_0.001.pth \
    --model model_lr_0.0005.pth \
    --model model_lr_0.0001.pth \
    --test-data data.json
```

## Integration with Qobuzarr

### Option 1: Replace Existing Model

```bash
# 1. Copy your generated model
cp MyPersonalizedMLOptimizer.cs ../src/Indexers/

# 2. Edit QobuzIndexer.cs
# Replace:
_patternLearningEngine = new Lazy<IPatternLearningEngine>(() => new CompiledMLQueryOptimizer(logger));
# With:
_patternLearningEngine = new Lazy<IPatternLearningEngine>(() => new MyPersonalizedMLOptimizer(logger));

# 3. Rebuild
cd ..
./build.sh --deploy
```

### Option 2: A/B Testing

Keep both models and switch dynamically:

```csharp
// In QobuzIndexerSettings.cs
[FieldDefinition(17, Label = "Use Personalized Model", Type = FieldType.Checkbox)]
public bool UsePersonalizedModel { get; set; }

// In QobuzIndexer.cs
_patternLearningEngine = new Lazy<IPatternLearningEngine>(() => 
    Settings.UsePersonalizedModel 
        ? new MyPersonalizedMLOptimizer(logger)
        : new CompiledMLQueryOptimizer(logger)
);
```

### Option 3: Ensemble Model

Combine multiple models:

```csharp
public class EnsembleMLOptimizer : IPatternLearningEngine
{
    private readonly IPatternLearningEngine[] _models;
    
    public QueryComplexity PredictComplexity(string artist, string album)
    {
        var predictions = _models.Select(m => m.PredictComplexity(artist, album)).ToArray();
        // Return majority vote or weighted average
        return GetMajorityVote(predictions);
    }
}
```

## Performance Tuning

### GPU Optimization

**Memory optimization:**
```bash
# Reduce batch size if you get CUDA out of memory
python train_ml_model.py --batch-size 128

# Monitor GPU usage
nvidia-smi -l 1
```

**Mixed precision training (faster):**
```python
# In train_ml_model.py, add:
from torch.cuda.amp import autocast, GradScaler

scaler = GradScaler()

# In training loop:
with autocast():
    outputs = model(batch_features)
    loss = criterion(outputs, batch_labels)

scaler.scale(loss).backward()
scaler.step(optimizer)
scaler.update()
```

### Model Architecture Tuning

**Wider networks (more accuracy):**
```bash
python train_ml_model.py --hidden-sizes 512 256 128 64
```

**Deeper networks (more capacity):**
```bash
python train_ml_model.py --hidden-sizes 128 128 128 64 32
```

**Regularization (prevent overfitting):**
```json
{
  "training": {
    "dropout_rate": 0.5,
    "weight_decay": 0.001
  }
}
```

### Data Augmentation

Increase training data variety:

```python
# In MLTrainingDataGenerator.py
def augment_album_data(self, album):
    variations = [album]
    
    # Add cleaned versions
    cleaned_title = re.sub(r'\([^)]*\)', '', album['album_title']).strip()
    if cleaned_title != album['album_title']:
        variations.append({**album, 'album_title': cleaned_title})
    
    # Add with/without 'The'
    if album['artist_name'].startswith('The '):
        no_the = album['artist_name'][4:]
        variations.append({**album, 'artist_name': no_the})
    
    return variations
```

## Performance Benchmarks

### Expected Results

**Baseline (rule-based):**
- Simple queries: 85% accuracy
- Medium queries: 60% accuracy
- Complex queries: 90% accuracy
- Overall: ~75% accuracy

**Well-trained ML model:**
- Simple queries: 92% accuracy
- Medium queries: 78% accuracy  
- Complex queries: 94% accuracy
- Overall: ~87% accuracy

**API call reduction:**
- Baseline: 45-55%
- Good ML model: 60-70%
- Excellent ML model: 75%+

### Monitoring Model Performance

Add telemetry to track real-world performance:

```csharp
// In your custom optimizer
public void RecordResult(string artist, string album, QueryComplexity used, bool success)
{
    // Log to file for analysis
    var logEntry = new {
        Timestamp = DateTime.UtcNow,
        Artist = artist,
        Album = album,
        PredictedComplexity = PredictComplexity(artist, album),
        UsedComplexity = used,
        Success = success
    };
    
    File.AppendAllText("ml_performance.jsonl", JsonConvert.SerializeObject(logEntry) + "\n");
}
```

Analyze the logs:

```bash
# Extract performance data
python -c "
import json
with open('ml_performance.jsonl') as f:
    data = [json.loads(line) for line in f]
    
correct = sum(1 for d in data if d['PredictedComplexity'] == d['UsedComplexity'] and d['Success'])
total = len([d for d in data if d['Success']])
print(f'Real-world accuracy: {correct/total:.2%}')
"
```

## Troubleshooting

### Common Issues

**CUDA out of memory:**
```bash
# Reduce batch size
python train_ml_model.py --batch-size 64

# Or use CPU
python train_ml_model.py --cpu
```

**MusicBrainz connection failed:**
```bash
# Test connection
curl http://192.168.2.13:5001/ws/2/artist/5b11f4ce-a62d-471e-81fc-a69a8278c7da

# Check firewall/network
ping 192.168.2.13
```

**Poor model performance:**
- **More data**: Increase `--max-albums`
- **Longer training**: Increase `--epochs`
- **Better features**: Modify feature extraction
- **Data quality**: Check for duplicate/invalid albums

**Model won't export:**
```bash
# Check PyTorch model file
python -c "import torch; print(torch.load('model.pth').keys())"

# Verify all components are saved
python export_model_to_csharp.py --model model.pth --output test.cs
```

### Debugging Tips

**Verbose logging:**
```bash
# Enable debug logging
export PYTHONPATH=.
python -c "import logging; logging.basicConfig(level=logging.DEBUG)" train_ml_model.py
```

**Check GPU utilization:**
```bash
# Monitor during training
watch -n 1 nvidia-smi
```

**Validate intermediate results:**
```bash
# Check extraction quality
python -c "
import json
with open('data.json') as f:
    data = json.load(f)
    albums = data['albums']
    print(f'Albums: {len(albums)}')
    print(f'Artists: {len(set(a[\"artist_name\"] for a in albums))}')
    print(f'Sample: {albums[0]}')
"
```

## Community Contributions

### Sharing Your Model

Help improve Qobuzarr for everyone:

```bash
# Anonymize your training data
python -c "
import json
with open('my_data.json') as f:
    data = json.load(f)
    
# Remove personal info, keep patterns
for album in data['albums']:
    album['artist_name'] = 'Artist_' + str(hash(album['artist_name']) % 10000)
    album['album_title'] = 'Album_' + str(hash(album['album_title']) % 10000)

with open('anonymous_patterns.json', 'w') as f:
    json.dump(data, f)
"

# Share anonymous patterns
# Submit to Qobuzarr project as GitHub issue
```

### Feature Requests

Suggest improvements:
- New feature engineering ideas
- Alternative model architectures  
- Integration enhancements
- Performance optimizations

## Advanced Topics

### Cross-Validation

Robust model evaluation:

```bash
# 5-fold cross-validation
python -c "
from sklearn.model_selection import StratifiedKFold
# ... implement cross-validation training
"
```

### Model Ensembles

Combine multiple models:

```bash
# Train multiple models with different random seeds
for seed in 42 123 456; do
    python train_ml_model.py \
        --input data.json \
        --output model_seed_$seed.pth \
        --random-seed $seed
done

# Create ensemble
python create_ensemble.py \
    --models model_seed_*.pth \
    --output ensemble_model.cs
```

### Active Learning

Improve model with targeted training:

```python
# Identify uncertain predictions
def find_uncertain_samples(model, albums):
    uncertainties = []
    for album in albums:
        probs = model.predict_probabilities(album)
        uncertainty = 1 - max(probs)  # 1 - confidence
        uncertainties.append((uncertainty, album))
    
    # Return most uncertain samples for manual labeling
    return sorted(uncertainties, reverse=True)[:100]
```

---

## Summary

This comprehensive guide covers everything from quick 5-minute training to advanced model optimization. Your RTX 3090 provides excellent acceleration for training personalized models that learn the specific patterns in your music library.

**Quick wins:**
1. Use `quick_start.py` for immediate results
2. Extract from your MusicBrainz instance for personalized data
3. GPU acceleration makes training fast and practical
4. Generated C# code integrates seamlessly with zero dependencies

**Next steps:**
- Experiment with different configurations
- Monitor real-world performance
- Consider contributing anonymized patterns back to the project

Happy training! 🚀🧠