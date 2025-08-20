# 🎵 Continuous ML Data Collection System

## Overview

This system continuously collects album metadata from various sources to build large, high-quality training datasets for achieving 95%+ ML classification accuracy. It runs autonomously and automatically retrains models as data grows.

## 🚀 Quick Start

### Option 1: One-Command Setup
```bash
# For 95% accuracy with 10,000 albums (recommended)
python start_collection.py

# For quick testing with 2,000 albums
python start_collection.py --quick

# For maximum accuracy with 25,000 albums
python start_collection.py --massive
```

### Option 2: Custom Configuration
```bash
# Custom target size and accuracy
python start_collection.py --target 15000 --accuracy 0.96

# Resume interrupted collection
python start_collection.py --resume
```

## 📊 Monitoring Progress

### Real-time Dashboard
```bash
# Monitor collection in another terminal
python monitor_collection.py

# Monitor with custom targets
python monitor_collection.py --target 15000 --accuracy 0.96
```

The dashboard shows:
- Live progress bars and completion estimates
- Collection rate (albums/hour)
- Data quality metrics
- Source distribution
- Complexity classification breakdown
- ETA and projections

## 🎯 Collection Presets

| Preset | Albums | Target Accuracy | Estimated Time | Command |
|--------|--------|----------------|----------------|---------|
| **Quick** | 2,000 | 90% | 1 hour | `--quick` |
| **Medium** | 5,000 | 93% | 2.5 hours | `--medium` |
| **Large** | 10,000 | 95% | 5 hours | `--large` |
| **Massive** | 25,000 | 97% | 12+ hours | `--massive` |

## 🔧 Advanced Usage

### Background Collection
```bash
# Run collection in background (Linux/Mac)
nohup python start_collection.py --large > collection.log 2>&1 &

# Windows PowerShell background
Start-Process python -ArgumentList "start_collection.py --large" -WindowStyle Hidden
```

### Multiple Sources
The system automatically collects from:
- **MusicBrainz API** (30%): Real album metadata with high accuracy
- **Generated Patterns** (70%): Realistic synthetic data for volume

### Database Management
```bash
# Check collection status
sqlite3 album_collection.db "SELECT COUNT(*), source FROM albums GROUP BY source;"

# Export current dataset manually
python -c "
from continuous_data_collector import ContinuousCollector
collector = ContinuousCollector()
collector.export_training_dataset('my_dataset.json')
"
```

## 📈 Performance Expectations

### Accuracy by Dataset Size
| Albums | Expected Accuracy | Notes |
|--------|------------------|-------|
| 500 | 85-90% | Good baseline |
| 2,000 | 90-93% | Production ready |
| 5,000 | 93-95% | High quality |
| 10,000+ | 95-97% | Excellent |
| 25,000+ | 97%+ | State-of-the-art |

### Collection Rate
- **MusicBrainz**: ~200-500 albums/hour (rate limited)
- **Generated**: ~2,000+ albums/hour (unlimited)
- **Combined**: ~800-1,500 albums/hour typical

## 🛠️ Troubleshooting

### Common Issues

**Collection Stops/Errors**
```bash
# Check logs
tail -f data_collection_*.log

# Resume collection
python start_collection.py --resume
```

**Low Collection Rate**
- MusicBrainz rate limiting (normal)
- Network connectivity issues
- Increase generated data ratio

**Database Corruption**
```bash
# Backup and restart
mv album_collection.db album_collection_backup.db
python start_collection.py
```

**Memory Usage**
- SQLite database grows ~1MB per 1,000 albums
- Peak memory: ~500MB for training
- Disk space: ~100MB per 10,000 albums

### Performance Optimization

**Speed Up Collection**
- Use `--quick` for faster initial results
- Focus on generated data for volume
- Run on faster CPU/SSD

**Improve Accuracy**
- Collect more complex albums (compilations, box sets)
- Add more diverse genres
- Increase dataset size gradually

## 🔄 Automated Training Pipeline

### How It Works
1. **Continuous Collection**: Gathers albums from multiple sources
2. **Automatic Deduplication**: Prevents duplicate entries
3. **Progressive Training**: Retrains model every 1,000 albums
4. **Quality Monitoring**: Tracks accuracy improvements
5. **Auto-Stop**: Stops when target accuracy reached

### Training Schedule
- **Retrain Trigger**: Every 1,000 new albums
- **Training Time**: 1-5 minutes per retrain
- **Model Checkpoints**: Automatic versioning
- **Final Export**: Complete dataset + best model

## 📁 Output Files

### Generated Files
```
album_collection.db          # SQLite database with all albums
final_training_dataset.json  # Complete dataset for training
model_YYYYMMDD_HHMMSS.pth   # Trained PyTorch models
training_dataset_*.json     # Incremental dataset exports
data_collection_*.log       # Detailed collection logs
```

### Integration with Qobuzarr
1. Copy best `.pth` model to `scripts/`
2. Update configuration to use new model
3. Restart Lidarr to load new model
4. Monitor performance in Qobuzarr logs

## 🎮 Interactive Usage

### Real-time Control
- **Ctrl+C**: Graceful shutdown (saves progress)
- **Monitor**: Live dashboard with statistics
- **Resume**: Automatic progress restoration
- **Logs**: Detailed activity tracking

### Quality Assurance
- **Duplicate Detection**: Hash-based deduplication
- **Source Validation**: Multiple data quality checks
- **Progress Tracking**: Real-time accuracy monitoring
- **Automatic Backup**: Continuous database saves

## 🔮 Future Enhancements

### Planned Features
- **Multi-API Support**: Last.fm, Spotify, Discogs integration
- **Smart Scheduling**: Optimal collection timing
- **Cloud Integration**: Remote training capabilities
- **Advanced Filtering**: Genre/year/complexity targeting

### Contributing Data Sources
The system is designed to easily add new data sources:
1. Implement new collector method
2. Add to collection strategy
3. Test with small dataset
4. Deploy at scale

## 📞 Support

### Getting Help
- Check logs: `data_collection_*.log`
- Monitor dashboard: `python monitor_collection.py`
- Database stats: SQLite browser tools
- Community: Qobuzarr project discussions

### Performance Tuning
- Adjust batch sizes for your system
- Monitor network usage
- Balance API calls vs. generated data
- Scale collection workers as needed

---

**🎯 Recommendation**: Start with `python start_collection.py --large` for best balance of speed and accuracy. Monitor with the dashboard and let it run overnight for excellent results!