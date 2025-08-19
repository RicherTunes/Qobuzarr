#!/usr/bin/env python3
"""
Quick Start Script for ML Training Pipeline

One-command script to extract data, train model, and export to C#
for users who want to quickly train a personalized model.

Usage:
    python quick_start.py --mb-url http://192.168.2.13:5001/
    python quick_start.py --config config.json --fast-mode
"""

import argparse
import json
import logging
import os
import sys
import subprocess
from pathlib import Path
from datetime import datetime

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class QuickStartTrainer:
    """Complete ML training pipeline in one script"""
    
    def __init__(self, config_path: str = None):
        self.config = self._load_config(config_path)
        self.output_dir = Path("training_output")
        self.output_dir.mkdir(exist_ok=True)
        
    def _load_config(self, config_path: str) -> dict:
        """Load configuration"""
        if config_path and os.path.exists(config_path):
            with open(config_path, 'r') as f:
                return json.load(f)
        
        # Default configuration
        return {
            "musicbrainz": {
                "url": "http://192.168.2.13:5001/",
                "max_albums": 10000
            },
            "training": {
                "epochs": 50,
                "batch_size": 256,
                "use_gpu": True
            },
            "output": {
                "class_name": "PersonalizedMLQueryOptimizer"
            }
        }
    
    def run_command(self, cmd: list, description: str) -> bool:
        """Run a command with logging"""
        logger.info(f"🚀 {description}")
        logger.info(f"Command: {' '.join(cmd)}")
        
        try:
            result = subprocess.run(cmd, check=True, capture_output=True, text=True)
            if result.stdout:
                logger.info(f"Output: {result.stdout.strip()}")
            return True
        except subprocess.CalledProcessError as e:
            logger.error(f"❌ Failed: {e}")
            if e.stderr:
                logger.error(f"Error: {e.stderr}")
            return False
    
    def step1_extract_data(self) -> str:
        """Step 1: Extract MusicBrainz data"""
        output_file = self.output_dir / "musicbrainz_albums.json"
        
        cmd = [
            sys.executable, "extract_musicbrainz_data.py",
            "--mb-url", self.config["musicbrainz"]["url"],
            "--output", str(output_file),
            "--max-albums", str(self.config["musicbrainz"]["max_albums"])
        ]
        
        success = self.run_command(cmd, "Extracting MusicBrainz data")
        return str(output_file) if success else None
    
    def step2_train_model(self, data_file: str) -> str:
        """Step 2: Train ML model"""
        model_file = self.output_dir / "trained_model.pth"
        
        cmd = [
            sys.executable, "train_ml_model.py",
            "--input", data_file,
            "--output", str(model_file),
            "--epochs", str(self.config["training"]["epochs"]),
            "--batch-size", str(self.config["training"]["batch_size"])
        ]
        
        if self.config["training"]["use_gpu"]:
            cmd.append("--gpu")
        else:
            cmd.append("--cpu")
        
        success = self.run_command(cmd, "Training ML model")
        return str(model_file) if success else None
    
    def step3_export_csharp(self, model_file: str) -> str:
        """Step 3: Export to C# code"""
        csharp_file = self.output_dir / f"{self.config['output']['class_name']}.cs"
        
        cmd = [
            sys.executable, "export_model_to_csharp.py",
            "--model", model_file,
            "--output", str(csharp_file),
            "--class", self.config['output']['class_name']
        ]
        
        success = self.run_command(cmd, "Exporting to C# code")
        return str(csharp_file) if success else None
    
    def step4_validate_model(self, model_file: str, data_file: str) -> bool:
        """Step 4: Validate model performance"""
        cmd = [
            sys.executable, "validate_model.py",
            "--model", model_file,
            "--test-data", data_file,
            "--baseline",
            "--benchmark",
            "--output-dir", str(self.output_dir)
        ]
        
        return self.run_command(cmd, "Validating model performance")
    
    def generate_integration_guide(self, csharp_file: str):
        """Generate integration guide"""
        guide_content = f\"\"\"# Integration Guide\n\nYour personalized ML model has been generated: {csharp_file}\n\n## Integration Steps\n\n1. **Copy the generated file:**\n   ```bash\n   cp \"{csharp_file}\" \"../src/Indexers/\"\n   ```\n\n2. **Update QobuzIndexer.cs:**\n   Replace the line:\n   ```csharp\n   _patternLearningEngine = new Lazy<IPatternLearningEngine>(() => new CompiledMLQueryOptimizer(logger));\n   ```\n   With:\n   ```csharp\n   _patternLearningEngine = new Lazy<IPatternLearningEngine>(() => new {self.config['output']['class_name']}(logger));\n   ```\n\n3. **Rebuild the plugin:**\n   ```bash\n   cd ..\n   ./build.sh --deploy\n   ```\n\n4. **Test the plugin:**\n   - Restart Lidarr\n   - Enable \"Query Intelligence\" and \"ML Predictions\" in Qobuzarr settings\n   - Monitor logs for ML prediction messages\n\n## Performance Comparison\n\nCheck the validation results in `{self.output_dir}/performance_report.json` to see how your personalized model compares to the baseline.\n\n## Retraining\n\nTo retrain with more data or different settings:\n1. Edit `config.json` with new parameters\n2. Run `python quick_start.py --config config.json`\n\n## Support\n\nIf you encounter issues, check:\n- MusicBrainz instance is running at {self.config['musicbrainz']['url']}\n- GPU drivers are installed (if using --gpu)\n- All dependencies are installed (`pip install -r requirements.txt`)\n\"\"\"\n        \n        guide_file = self.output_dir / \"integration_guide.md\"\n        with open(guide_file, 'w') as f:\n            f.write(guide_content)\n        \n        logger.info(f\"📋 Integration guide saved to: {guide_file}\")\n    \n    def run_full_pipeline(self) -> bool:\n        \"\"\"Run the complete training pipeline\"\"\"\n        logger.info(\"🚀 Starting Quick Start ML Training Pipeline\")\n        logger.info(f\"Output directory: {self.output_dir}\")\n        logger.info(f\"MusicBrainz URL: {self.config['musicbrainz']['url']}\")\n        \n        # Step 1: Extract data\n        data_file = self.step1_extract_data()\n        if not data_file:\n            logger.error(\"❌ Data extraction failed\")\n            return False\n        \n        # Step 2: Train model\n        model_file = self.step2_train_model(data_file)\n        if not model_file:\n            logger.error(\"❌ Model training failed\")\n            return False\n        \n        # Step 3: Export to C#\n        csharp_file = self.step3_export_csharp(model_file)\n        if not csharp_file:\n            logger.error(\"❌ C# export failed\")\n            return False\n        \n        # Step 4: Validate model\n        validation_success = self.step4_validate_model(model_file, data_file)\n        if not validation_success:\n            logger.warning(\"⚠️ Model validation had issues, but continuing...\")\n        \n        # Generate integration guide\n        self.generate_integration_guide(csharp_file)\n        \n        # Success summary\n        logger.info(\"\\n\" + \"=\"*60)\n        logger.info(\"✅ QUICK START COMPLETED SUCCESSFULLY!\")\n        logger.info(\"=\"*60)\n        logger.info(f\"📁 All outputs saved to: {self.output_dir}\")\n        logger.info(f\"🧠 Your personalized model: {csharp_file}\")\n        logger.info(f\"📊 Performance report: {self.output_dir}/performance_report.json\")\n        logger.info(f\"📋 Integration guide: {self.output_dir}/integration_guide.md\")\n        logger.info(\"\\nNext: Follow the integration guide to use your personalized model!\")\n        \n        return True\n\ndef main():\n    parser = argparse.ArgumentParser(description=\"Quick start ML training pipeline\")\n    parser.add_argument('--mb-url', help='MusicBrainz instance URL')\n    parser.add_argument('--config', help='Configuration file path')\n    parser.add_argument('--fast-mode', action='store_true', \n                       help='Use reduced settings for faster training')\n    parser.add_argument('--max-albums', type=int, default=10000,\n                       help='Maximum albums to extract')\n    parser.add_argument('--epochs', type=int, default=50,\n                       help='Training epochs')\n    parser.add_argument('--class-name', default='PersonalizedMLQueryOptimizer',\n                       help='C# class name for generated model')\n    \n    args = parser.parse_args()\n    \n    # Create config from arguments\n    config = None\n    if args.config:\n        config = args.config\n    else:\n        # Create temporary config\n        temp_config = {\n            \"musicbrainz\": {\n                \"url\": args.mb_url or \"http://192.168.2.13:5001/\",\n                \"max_albums\": 1000 if args.fast_mode else args.max_albums\n            },\n            \"training\": {\n                \"epochs\": 10 if args.fast_mode else args.epochs,\n                \"batch_size\": 128 if args.fast_mode else 256,\n                \"use_gpu\": True\n            },\n            \"output\": {\n                \"class_name\": args.class_name\n            }\n        }\n        \n        config_file = \"temp_quick_start_config.json\"\n        with open(config_file, 'w') as f:\n            json.dump(temp_config, f, indent=2)\n        config = config_file\n    \n    # Check prerequisites\n    scripts_dir = Path(__file__).parent\n    required_scripts = [\n        \"extract_musicbrainz_data.py\",\n        \"train_ml_model.py\",\n        \"export_model_to_csharp.py\",\n        \"validate_model.py\"\n    ]\n    \n    for script in required_scripts:\n        if not (scripts_dir / script).exists():\n            logger.error(f\"❌ Required script not found: {script}\")\n            sys.exit(1)\n    \n    # Run pipeline\n    trainer = QuickStartTrainer(config)\n    success = trainer.run_full_pipeline()\n    \n    if not success:\n        logger.error(\"❌ Quick start pipeline failed\")\n        sys.exit(1)\n\nif __name__ == '__main__':\n    main()