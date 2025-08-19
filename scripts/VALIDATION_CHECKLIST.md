# Validation Checklist for ML Training Scripts

Use this checklist to verify your scripts are working correctly before spending time on full training.

## ✅ **Pre-Training Validation**

### **Environment Setup**
```bash
# Test basic environment
python setup_environment.py --gpu-check

# Expected output:
# ✅ Python 3.x.x is compatible
# ✅ pip is available  
# ✅ Dependencies installed successfully
# ✅ NVIDIA GPU detected: [Your GPU]
# ✅ PyTorch CUDA support is available
```

### **Script Functionality**
```bash
# Test all scripts with mock data (no MusicBrainz needed)
python test_scripts.py --mock-musicbrainz

# Expected output:
# ✅ Environment Setup: PASSED
# ✅ Data Extraction: PASSED
# ✅ Feature Extraction: PASSED  
# ✅ Model Training: PASSED
# ✅ C# Export: PASSED
# 🎉 All tests passed! The ML training pipeline is ready to use.
```

### **MusicBrainz Connection**
```bash
# Test connection to your MusicBrainz instance
curl http://192.168.2.13:5001/ws/2/artist/5b11f4ce-a62d-471e-81fc-a69a8278c7da

# Expected: JSON response with artist data (not error page)
```

## ✅ **Quick Training Test**

### **Fast Test Run**
```bash
# 5-minute test to verify everything works
python quick_start.py --mb-url http://192.168.2.13:5001/ --fast-mode

# Expected files created:
# training_output/musicbrainz_albums.json (500+ albums)
# training_output/trained_model.pth (PyTorch model)
# training_output/PersonalizedMLQueryOptimizer.cs (C# code)
# training_output/integration_guide.md (instructions)
```

### **Validate Generated Files**

**Check dataset quality:**
```bash
python -c "
import json
with open('training_output/musicbrainz_albums.json') as f:
    data = json.load(f)
    albums = data['albums']
    print(f'Albums: {len(albums)}')
    print(f'Artists: {len(set(a[\"artist_name\"] for a in albums))}')
    print(f'Years: {min(int(a[\"release_year\"]) for a in albums if a[\"release_year\"])} - {max(int(a[\"release_year\"]) for a in albums if a[\"release_year\"])}')
"

# Expected output:
# Albums: 500+ 
# Artists: 300+ (diverse artists)
# Years: 1950 - 2024 (wide range)
```

**Check model file:**
```bash
python -c "
import torch
model = torch.load('training_output/trained_model.pth', map_location='cpu')
print('Model components:', list(model.keys()))
print('Has model state:', 'model_state_dict' in model)
print('Has feature extractor:', 'feature_extractor' in model)
"

# Expected output:
# Model components: ['model_state_dict', 'feature_extractor', 'label_encoder', ...]
# Has model state: True
# Has feature extractor: True
```

**Check C# code quality:**
```bash
grep -c "class PersonalizedMLQueryOptimizer" training_output/PersonalizedMLQueryOptimizer.cs
grep -c "IPatternLearningEngine" training_output/PersonalizedMLQueryOptimizer.cs  
grep -c "PredictComplexity" training_output/PersonalizedMLQueryOptimizer.cs

# Expected output (each should be > 0):
# 1 (class declaration)
# 1+ (interface implementation)  
# 1+ (main prediction method)
```

## ✅ **Performance Validation**

### **Training Speed Check**
| Hardware | Quick Test (500 albums, 5 epochs) | Expected Time |
|----------|-----------------------------------|---------------|
| RTX 3090 | GPU training | 2-3 minutes |
| RTX 4090 | GPU training | 1-2 minutes |
| CPU only | CPU training | 8-15 minutes |

### **Model Quality Check**
```bash
# Run validation script
python validate_model.py \
  --model training_output/trained_model.pth \
  --test-data training_output/musicbrainz_albums.json \
  --baseline

# Expected metrics:
# Test Accuracy: 0.75+ (75%+)
# Better than baseline: Yes
# No crashes or errors
```

## ✅ **Integration Validation**

### **C# Code Compilation Test**
```bash
# Copy to source directory (test integration)
cp training_output/PersonalizedMLQueryOptimizer.cs ../src/Indexers/

# Quick syntax check (if C# compiler available)
# csc /t:library /r:path/to/Lidarr.dll PersonalizedMLQueryOptimizer.cs
```

### **Integration Guide Check**
```bash
# Verify integration guide was created
cat training_output/integration_guide.md

# Should contain:
# - Clear step-by-step instructions
# - Specific file paths
# - Code snippets for QobuzIndexer.cs changes
# - Build and test instructions
```

## ❌ **Red Flags (Fix Before Full Training)**

### **Environment Issues**
❌ GPU not detected (will be very slow)  
❌ Missing Python packages  
❌ CUDA version mismatch  
❌ Insufficient disk space  

### **Data Issues**
❌ Less than 100 albums extracted  
❌ All albums from same artist/year  
❌ MusicBrainz connection errors  
❌ Empty or corrupted JSON files  

### **Training Issues**
❌ Model accuracy below 65%  
❌ Training crashes with out-of-memory  
❌ Takes longer than expected (see timing table)  
❌ Generated C# code missing key methods  

### **Integration Issues**
❌ C# file doesn't compile  
❌ Missing IPatternLearningEngine interface  
❌ Integration guide incomplete  
❌ File paths don't match project structure  

## 🚀 **Green Light Indicators**

When you see these, you're ready for full training:

✅ All tests pass with mock data  
✅ Quick test completes in expected time  
✅ MusicBrainz extraction works smoothly  
✅ Model accuracy > 75% on test data  
✅ C# code compiles and looks correct  
✅ Integration guide is clear and complete  

## 🔧 **Common Fixes**

### **GPU Not Working**
```bash
# Reinstall CUDA-enabled PyTorch
python setup_environment.py --install-cuda
```

### **Memory Issues**
```bash
# Use smaller batches
python quick_start.py --fast-mode --profile development
```

### **Poor Accuracy**
```bash
# Need more diverse data - try larger profile
python quick_start.py --profile balanced
```

### **Connection Issues**
```bash
# Test MusicBrainz directly
curl -I http://192.168.2.13:5001/
# Check firewall/network settings
```

## 📊 **Success Metrics Dashboard**

After validation, track these metrics:

| Metric | Baseline | Target | Your Result |
|--------|----------|--------|-------------|
| Training time | N/A | < 30 min | _____ |
| Model accuracy | 75% | 80%+ | _____ |
| Albums extracted | N/A | 10,000+ | _____ |
| C# file size | N/A | 50-200 KB | _____ |
| Integration time | N/A | < 10 min | _____ |

## 📋 **Pre-Full-Training Checklist**

Before running full `high_quality` or `exhaustive` training:

- [ ] Quick test completed successfully
- [ ] GPU training working (not CPU fallback)
- [ ] MusicBrainz has 10,000+ albums available
- [ ] Validation accuracy > 75%
- [ ] C# export working correctly
- [ ] Integration guide makes sense
- [ ] Have 2+ hours available (for full training)
- [ ] Disk space > 20GB free

## 🎯 **Ready for Production**

When all validations pass, you're ready to:

1. Run full training with your chosen profile
2. Integrate the generated model into Qobuzarr
3. Monitor real-world performance improvements
4. Share your results with the community

---

**Remember**: Validation saves time. 15 minutes of validation prevents hours of wasted full training on broken setups.