#!/usr/bin/env python3
"""
Library Analysis Pipeline Orchestrator

Runs the complete pipeline to extract your Lidarr library, analyze Unicode gaps,
validate against Qobuz, and generate comprehensive test cases.

Usage:
    python scripts/run_library_analysis_pipeline.py --full-pipeline
    python scripts/run_library_analysis_pipeline.py --quick-test --limit 100
    python scripts/run_library_analysis_pipeline.py --extract-only
"""

import asyncio
import subprocess
import sys
import argparse
import json
from pathlib import Path
import logging
from datetime import datetime

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class LibraryAnalysisPipeline:
    """Orchestrates the complete library analysis pipeline"""
    
    def __init__(self, lidarr_url: str, api_key: str):
        self.lidarr_url = lidarr_url
        self.api_key = api_key
        self.scripts_dir = Path("scripts")
        
    async def run_full_pipeline(self, limit: Optional[int] = None, 
                               qobuz_validation: bool = True):
        """Run the complete analysis pipeline"""
        
        logger.info("🚀 Starting full library analysis pipeline")
        logger.info("=" * 60)
        
        # Step 1: Extract Lidarr library
        logger.info("📖 Step 1: Extracting Lidarr library...")
        extraction_success = await self.run_library_extraction(limit)
        
        if not extraction_success:
            logger.error("❌ Library extraction failed - aborting pipeline")
            return False
        
        # Step 2: Analyze Unicode gaps
        logger.info("🔍 Step 2: Analyzing Unicode system gaps...")
        gap_analysis_success = await self.run_gap_analysis()
        
        if not gap_analysis_success:
            logger.error("❌ Gap analysis failed - aborting pipeline") 
            return False
        
        # Step 3: Validate gaps against Qobuz (optional)
        if qobuz_validation:
            logger.info("✅ Step 3: Validating gaps against Qobuz API...")
            validation_success = await self.run_qobuz_validation()
            
            if not validation_success:
                logger.warning("⚠️ Qobuz validation failed - continuing without validation")
        
        # Step 4: Generate test cases
        logger.info("🧪 Step 4: Generating complex test cases...")
        test_generation_success = await self.run_test_generation()
        
        if not test_generation_success:
            logger.error("❌ Test generation failed")
            return False
        
        # Step 5: Summary report
        await self.generate_pipeline_summary()
        
        logger.info("🎉 Pipeline completed successfully!")
        return True
    
    async def run_library_extraction(self, limit: Optional[int] = None) -> bool:
        """Run library extraction step"""
        
        cmd = [
            sys.executable, 'scripts/extract_lidarr_library.py',
            '--lidarr-url', self.lidarr_url,
            '--api-key', self.api_key,
            '--complexity-threshold', '0.3',
            '--export-format', 'database',
            '--output', 'scripts/pipeline_library_analysis'
        ]
        
        if limit:
            cmd.extend(['--limit', str(limit)])
        
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
            
            if result.returncode == 0:
                logger.info("✅ Library extraction completed")
                logger.info(f"📊 Output: {result.stdout.split('💾')[-1].strip() if '💾' in result.stdout else 'Check scripts/ directory'}")
                return True
            else:
                logger.error(f"❌ Library extraction failed: {result.stderr}")
                return False
                
        except subprocess.TimeoutExpired:
            logger.error("❌ Library extraction timed out (10 minutes)")
            return False
        except Exception as e:
            logger.error(f"❌ Library extraction error: {e}")
            return False
    
    async def run_gap_analysis(self) -> bool:
        """Run Unicode gap analysis step"""
        
        cmd = [
            sys.executable, 'scripts/analyze_unicode_gaps.py',
            '--database', 'scripts/pipeline_library_analysis.db',
            '--min-complexity', '0.4',
            '--output', 'scripts/pipeline_gaps_analysis.json'
        ]
        
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
            
            if result.returncode == 0:
                logger.info("✅ Gap analysis completed")
                return True
            else:
                logger.error(f"❌ Gap analysis failed: {result.stderr}")
                return False
                
        except Exception as e:
            logger.error(f"❌ Gap analysis error: {e}")
            return False
    
    async def run_qobuz_validation(self) -> bool:
        """Run Qobuz validation step (requires credentials)"""
        
        # Check for Qobuz credentials
        qobuz_app_id = os.getenv('QOBUZ_APP_ID')
        qobuz_app_secret = os.getenv('QOBUZ_APP_SECRET')
        
        if not qobuz_app_id or not qobuz_app_secret:
            logger.warning("⚠️ Qobuz credentials not found - skipping validation")
            logger.warning("Set QOBUZ_APP_ID and QOBUZ_APP_SECRET environment variables")
            return False
        
        cmd = [
            sys.executable, 'scripts/validate_qobuz_gaps.py',
            '--gaps-file', 'scripts/pipeline_gaps_analysis.json',
            '--max-validations', '20',  # Limit for pipeline run
            '--output', 'scripts/pipeline_validation_results.json'
        ]
        
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)  # 30 minutes
            
            if result.returncode == 0:
                logger.info("✅ Qobuz validation completed")
                return True
            else:
                logger.error(f"❌ Qobuz validation failed: {result.stderr}")
                return False
                
        except subprocess.TimeoutExpired:
            logger.error("❌ Qobuz validation timed out")
            return False
        except Exception as e:
            logger.error(f"❌ Qobuz validation error: {e}")
            return False
    
    async def run_test_generation(self) -> bool:
        """Run test case generation step"""
        
        cmd = [
            sys.executable, 'scripts/generate_complex_test_cases.py',
            '--validation-results', 'scripts/pipeline_validation_results.json',
            '--gaps-analysis', 'scripts/pipeline_gaps_analysis.json',
            '--output', 'tests/Qobuzarr.Tests/Unit/Indexers/LibraryDerivedComplexTests.cs',
            '--max-test-cases', '30'
        ]
        
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
            
            if result.returncode == 0:
                logger.info("✅ Test case generation completed")
                return True
            else:
                logger.error(f"❌ Test generation failed: {result.stderr}")
                return False
                
        except Exception as e:
            logger.error(f"❌ Test generation error: {e}")
            return False
    
    async def generate_pipeline_summary(self):
        """Generate final pipeline summary report"""
        
        summary = {
            'pipeline_date': datetime.now().isoformat(),
            'steps_completed': [],
            'outputs_generated': [],
            'recommendations': []
        }
        
        # Check what outputs were generated
        outputs = [
            ('scripts/pipeline_library_analysis.db', 'Library extraction database'),
            ('scripts/pipeline_gaps_analysis.json', 'Unicode gaps analysis'),
            ('scripts/pipeline_validation_results.json', 'Qobuz validation results'),
            ('tests/Qobuzarr.Tests/Unit/Indexers/LibraryDerivedComplexTests.cs', 'Generated test cases')
        ]
        
        for output_path, description in outputs:
            if Path(output_path).exists():
                summary['outputs_generated'].append({
                    'file': output_path,
                    'description': description,
                    'size_kb': Path(output_path).stat().st_size // 1024
                })
                summary['steps_completed'].append(description)
        
        # Load gap analysis for summary
        try:
            if Path('scripts/pipeline_gaps_analysis.json').exists():
                with open('scripts/pipeline_gaps_analysis.json', 'r') as f:
                    gap_data = json.load(f)
                    
                summary['gap_analysis_summary'] = gap_data.get('report', {})
        except:
            pass
        
        # Load validation results for summary
        try:
            if Path('scripts/pipeline_validation_results.json').exists():
                with open('scripts/pipeline_validation_results.json', 'r') as f:
                    validation_data = json.load(f)
                    
                summary['validation_summary'] = validation_data.get('analysis', {})
        except:
            pass
        
        # Generate recommendations
        if summary.get('gap_analysis_summary', {}).get('total_gaps', 0) > 0:
            summary['recommendations'].append("Review identified gaps and implement Unicode system improvements")
        
        if summary.get('validation_summary', {}).get('validation_summary', {}).get('false_positives', 0) > 0:
            summary['recommendations'].append("Implement manual search strategies that worked for false positives")
        
        summary['recommendations'].append("Run generated test cases to validate Unicode system improvements")
        summary['recommendations'].append("Setup continuous monitoring for ongoing gap detection")
        
        # Save summary
        with open('scripts/pipeline_summary.json', 'w', encoding='utf-8') as f:
            json.dump(summary, f, indent=2, ensure_ascii=False)
        
        # Display summary
        print(f"\n📊 PIPELINE SUMMARY")
        print("=" * 60)
        print(f"🕐 Completed: {summary['pipeline_date']}")
        print(f"✅ Steps completed: {len(summary['steps_completed'])}")
        
        for step in summary['steps_completed']:
            print(f"   • {step}")
        
        print(f"\n📁 Outputs generated:")
        for output in summary['outputs_generated']:
            print(f"   • {output['file']} ({output['size_kb']} KB)")
        
        if summary.get('gap_analysis_summary'):
            gap_summary = summary['gap_analysis_summary']
            print(f"\n🔍 Gap Analysis Results:")
            print(f"   • Total gaps found: {gap_summary.get('total_gaps', 0)}")
            print(f"   • Severity distribution: {gap_summary.get('severity_distribution', {})}")
        
        if summary.get('validation_summary'):
            val_summary = summary['validation_summary']['validation_summary']
            print(f"\n✅ Validation Results:")
            print(f"   • Total validated: {val_summary.get('total_validated', 0)}")
            print(f"   • Confirmed gaps: {val_summary.get('confirmed_gaps', 0)}")
            print(f"   • False positives: {val_summary.get('false_positives', 0)}")
        
        print(f"\n🎯 Next Steps:")
        for rec in summary['recommendations']:
            print(f"   • {rec}")
        
        print(f"\n💾 Full summary saved to: scripts/pipeline_summary.json")

async def main():
    """Main pipeline orchestration"""
    parser = argparse.ArgumentParser(description="Run complete library analysis pipeline")
    parser.add_argument('--lidarr-url', default='http://192.168.2.50:8686',
                       help='Lidarr instance URL')
    parser.add_argument('--api-key', default='ca6a612bb8f84d9c976fcac967331da5',
                       help='Lidarr API key')
    parser.add_argument('--full-pipeline', action='store_true',
                       help='Run complete pipeline including Qobuz validation')
    parser.add_argument('--quick-test', action='store_true',
                       help='Run quick test with limited albums')
    parser.add_argument('--extract-only', action='store_true',
                       help='Only run library extraction step')
    parser.add_argument('--limit', type=int, default=None,
                       help='Limit number of albums to analyze')
    parser.add_argument('--skip-qobuz-validation', action='store_true',
                       help='Skip Qobuz API validation step')
    
    args = parser.parse_args()
    
    # Determine pipeline parameters
    if args.quick_test:
        limit = args.limit or 100
        qobuz_validation = not args.skip_qobuz_validation
        extract_only = False
    elif args.extract_only:
        limit = args.limit
        qobuz_validation = False
        extract_only = True
    else:
        limit = args.limit
        qobuz_validation = args.full_pipeline and not args.skip_qobuz_validation
        extract_only = False
    
    print("🚀 Lidarr Library Analysis Pipeline")
    print("=" * 60)
    print(f"🏠 Lidarr: {args.lidarr_url}")
    print(f"🔑 API Key: {args.api_key[:8]}...")
    print(f"📊 Limit: {limit or 'No limit'}")
    print(f"✅ Qobuz validation: {'Yes' if qobuz_validation else 'No'}")
    print(f"🎯 Mode: {'Extract only' if extract_only else 'Full pipeline'}")
    print("=" * 60)
    
    pipeline = LibraryAnalysisPipeline(args.lidarr_url, args.api_key)
    
    try:
        if extract_only:
            success = await pipeline.run_library_extraction(limit)
            if success:
                print("✅ Library extraction completed successfully!")
                print("Next: python scripts/analyze_unicode_gaps.py")
        else:
            success = await pipeline.run_full_pipeline(limit, qobuz_validation)
            if success:
                print("🎉 Complete pipeline finished successfully!")
                print("Check scripts/pipeline_summary.json for detailed results")
    
    except KeyboardInterrupt:
        logger.info("🛑 Pipeline stopped by user")
    except Exception as e:
        logger.error(f"💥 Pipeline failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())