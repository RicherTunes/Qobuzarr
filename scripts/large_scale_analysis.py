#!/usr/bin/env python3
"""
Large-Scale Unicode Analysis (50,000+ Albums)

Advanced analysis system designed to process tens of thousands of albums
to discover rare patterns and edge cases that only emerge at scale.

Usage:
    python scripts/large_scale_analysis.py --limit 50000
    python scripts/large_scale_analysis.py --comprehensive --include-rare-patterns
"""

import asyncio
import sqlite3
import json
import unicodedata
import re
from collections import defaultdict, Counter
from dataclasses import dataclass, asdict
from typing import List, Dict, Optional, Set, Tuple
import argparse
from pathlib import Path
import logging
from datetime import datetime
import statistics

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

@dataclass
class AdvancedPattern:
    """Advanced pattern discovered through large-scale analysis"""
    pattern_type: str
    pattern_value: str
    frequency: int
    affected_albums: List[str]
    failure_rate: float
    complexity_correlation: float
    recommended_action: str
    priority: str  # 'critical', 'high', 'medium', 'low'

@dataclass
class LargeScaleInsights:
    """Insights from large-scale analysis"""
    total_albums_analyzed: int
    rare_characters_discovered: List[AdvancedPattern]
    genre_specific_patterns: Dict[str, List[AdvancedPattern]]
    statistical_thresholds: Dict[str, float]
    unicode_script_coverage: Dict[str, int]
    complexity_distribution_detailed: Dict[str, int]
    improvement_recommendations: List[str]
    auto_enhancement_candidates: List[str]

class LargeScaleAnalyzer:
    """Advanced analyzer for processing 50,000+ albums"""
    
    def __init__(self):
        self.character_frequency = Counter()
        self.pattern_frequency = defaultdict(int)
        self.genre_patterns = defaultdict(lambda: defaultdict(int))
        self.complexity_by_genre = defaultdict(list)
        self.script_combinations = defaultdict(int)
        self.failure_patterns = defaultdict(list)
        
    async def analyze_large_library(self, database_path: str, limit: Optional[int] = None,
                                  include_rare_patterns: bool = True) -> LargeScaleInsights:
        """Analyze large library for advanced patterns"""
        
        logger.info(f"Starting large-scale analysis of library: {database_path}")
        logger.info(f"Target size: {limit or 'All albums'}")
        logger.info(f"Include rare patterns: {include_rare_patterns}")
        
        # Load library data  
        albums = self.load_large_dataset(database_path, limit)
        
        if not albums:
            raise ValueError("No albums found for analysis")
        
        logger.info(f"Loaded {len(albums)} albums for large-scale analysis")
        
        # Process in batches for memory efficiency
        batch_size = 1000
        processed_count = 0
        
        for i in range(0, len(albums), batch_size):
            batch = albums[i:i + batch_size]
            await self.process_batch(batch, include_rare_patterns)
            
            processed_count += len(batch)
            logger.info(f"Progress: {processed_count}/{len(albums)} albums processed")
        
        # Generate advanced insights
        insights = self.generate_large_scale_insights(albums)
        
        logger.info("Large-scale analysis complete")
        return insights
    
    def load_large_dataset(self, database_path: str, limit: Optional[int]) -> List[Dict]:
        """Load large dataset efficiently"""
        
        conn = sqlite3.connect(database_path)
        cursor = conn.cursor()
        
        # Get all albums with metadata
        query = '''
            SELECT * FROM library_albums 
            ORDER BY complexity_score DESC
        '''
        
        if limit:
            query += f' LIMIT {limit}'
        
        cursor.execute(query)
        columns = [description[0] for description in cursor.description]
        
        albums = []
        for row in cursor.fetchall():
            album_dict = dict(zip(columns, row))
            albums.append(album_dict)
        
        conn.close()
        return albums
    
    async def process_batch(self, batch: List[Dict], include_rare_patterns: bool):
        """Process a batch of albums for pattern discovery"""
        
        for album in batch:
            try:
                artist = album['artist']
                album_title = album['album']
                genre = album.get('genre', 'Unknown')
                complexity = album['complexity_score']
                
                full_text = f"{artist} {album_title}"
                
                # 1. Character frequency analysis
                for char in full_text:
                    if ord(char) > 127:
                        self.character_frequency[char] += 1
                
                # 2. Pattern frequency analysis
                patterns = self.extract_advanced_patterns(full_text)
                for pattern in patterns:
                    self.pattern_frequency[pattern] += 1
                
                # 3. Genre-specific pattern analysis
                genre_key = genre.split(',')[0].strip() if genre else 'Unknown'
                self.complexity_by_genre[genre_key].append(complexity)
                
                for pattern in patterns:
                    self.genre_patterns[genre_key][pattern] += 1
                
                # 4. Unicode script combination analysis
                scripts = self.detect_unicode_scripts(full_text)
                if len(scripts) > 1:
                    script_combo = '+'.join(sorted(scripts))
                    self.script_combinations[script_combo] += 1
                
                # 5. Rare pattern detection (only if enabled)
                if include_rare_patterns:
                    rare_patterns = self.detect_rare_patterns(full_text, complexity)
                    for pattern in rare_patterns:
                        self.failure_patterns[pattern].append({
                            'artist': artist,
                            'album': album_title,
                            'complexity': complexity
                        })
                
            except Exception as e:
                logger.warning(f"Error processing album {album.get('artist', 'Unknown')}: {e}")
                continue
    
    def extract_advanced_patterns(self, text: str) -> List[str]:
        """Extract advanced patterns that could cause search issues"""
        
        patterns = []
        
        # Unicode patterns
        if any(ord(c) > 127 for c in text):
            patterns.append('has_unicode')
            
            # Specific script patterns
            if any(0x4E00 <= ord(c) <= 0x9FFF for c in text):
                patterns.append('has_cjk')
            if any(0x0400 <= ord(c) <= 0x04FF for c in text):
                patterns.append('has_cyrillic')
            if any(0x0370 <= ord(c) <= 0x03FF for c in text):
                patterns.append('has_greek')
            if any(0x0590 <= ord(c) <= 0x05FF for c in text):
                patterns.append('has_hebrew')
            if any(0x0600 <= ord(c) <= 0x06FF for c in text):
                patterns.append('has_arabic')
        
        # Length patterns
        if len(text) > 100:
            patterns.append('very_long')
        if len(text) > 150:
            patterns.append('extremely_long')
        
        # Punctuation patterns
        special_char_count = sum(1 for c in text if not c.isalnum() and not c.isspace())
        if special_char_count > 10:
            patterns.append('punctuation_heavy')
        
        # Specific problematic patterns
        if re.search(r'\([^)]*\([^)]*\)', text):
            patterns.append('nested_parentheticals')
        
        if re.search(r'[^\w\s]{3,}', text):
            patterns.append('consecutive_symbols')
        
        if len(re.findall(r'\b(19|20)\d{2}\b', text)) > 1:
            patterns.append('multiple_years')
        
        if re.search(r'(vol|volume|pt|part|disc)\s*\.?\s*\d+', text, re.IGNORECASE):
            patterns.append('volume_series')
        
        # Edition complexity
        edition_count = len(re.findall(r'\b(deluxe|special|anniversary|remaster|edition|expanded)\b', text, re.IGNORECASE))
        if edition_count > 1:
            patterns.append('multiple_editions')
        
        # Featured artist complexity  
        feat_count = len(re.findall(r'\b(feat|ft|featuring)\.?\s+', text, re.IGNORECASE))
        if feat_count > 1:
            patterns.append('multiple_featured_artists')
        
        return patterns
    
    def detect_unicode_scripts(self, text: str) -> List[str]:
        """Detect Unicode scripts in text"""
        
        scripts = set()
        for char in text:
            if ord(char) > 127:
                try:
                    script_name = unicodedata.name(char).split()[0]
                    scripts.add(script_name)
                except ValueError:
                    scripts.add('UNKNOWN')
        
        # Simplify script names
        simplified = []
        for script in scripts:
            if 'LATIN' in script:
                simplified.append('Latin')
            elif 'CJK' in script or 'HIRAGANA' in script or 'KATAKANA' in script or 'HAN' in script:
                simplified.append('CJK')
            elif 'CYRILLIC' in script:
                simplified.append('Cyrillic')
            elif 'GREEK' in script:
                simplified.append('Greek')
            elif 'ARABIC' in script:
                simplified.append('Arabic')
            elif 'HEBREW' in script:
                simplified.append('Hebrew')
            else:
                simplified.append('Other')
        
        return list(set(simplified))
    
    def detect_rare_patterns(self, text: str, complexity: float) -> List[str]:
        """Detect rare patterns that only appear at scale"""
        
        rare_patterns = []
        
        # Mathematical symbols
        if re.search(r'[∞∑∆≠≤≥∈∉⊂⊃∪∩]', text):
            rare_patterns.append('mathematical_symbols')
        
        # Currency symbols beyond basic
        if re.search(r'[₿€£¥₹₽₩₪₫₵]', text):
            rare_patterns.append('currency_symbols')
        
        # Advanced typography
        if re.search(r'[""''‚„‹›«»]', text):
            rare_patterns.append('advanced_typography')
        
        # Musical notation
        if re.search(r'[♪♫♬♭♯♮]', text):
            rare_patterns.append('musical_notation')
        
        # Emoji (increasingly common in modern albums)
        if re.search(r'[\U0001F600-\U0001F64F\U0001F300-\U0001F5FF\U0001F680-\U0001F6FF\U0001F1E0-\U0001F1FF]', text):
            rare_patterns.append('emoji_content')
        
        # Complex number patterns
        if re.search(r'\b\d{4,}\b', text):  # 4+ digit numbers
            rare_patterns.append('complex_numbers')
        
        # Scientific notation
        if re.search(r'\d+\.\d+[eE][+-]?\d+', text):
            rare_patterns.append('scientific_notation')
        
        # Legal/trademark symbols
        if re.search(r'[™®©℗]', text):
            rare_patterns.append('legal_symbols')
        
        # Combining diacritics (different from precomposed)
        if re.search(r'[\u0300-\u036F]', text):
            rare_patterns.append('combining_diacritics')
        
        return rare_patterns
    
    def generate_large_scale_insights(self, albums: List[Dict]) -> LargeScaleInsights:
        """Generate comprehensive insights from large-scale analysis"""
        
        total_albums = len(albums)
        
        # 1. Identify rare characters that need mapping (frequency-based)
        rare_chars = []
        for char, freq in self.character_frequency.items():
            if freq >= 10:  # Character appears in 10+ albums
                failure_rate = self.estimate_failure_rate(char)
                
                rare_chars.append(AdvancedPattern(
                    pattern_type='character',
                    pattern_value=char,
                    frequency=freq,
                    affected_albums=[],  # Would be populated with full album list
                    failure_rate=failure_rate,
                    complexity_correlation=self.calculate_complexity_correlation(char, albums),
                    recommended_action=f"Add character mapping: {char} → ASCII equivalent",
                    priority='high' if freq > 50 else 'medium'
                ))
        
        # 2. Genre-specific insights
        genre_insights = {}
        for genre, complexity_list in self.complexity_by_genre.items():
            if len(complexity_list) > 10:  # Only analyze genres with 10+ albums
                avg_complexity = statistics.mean(complexity_list)
                max_complexity = max(complexity_list)
                
                genre_patterns = []
                for pattern, count in self.genre_patterns[genre].items():
                    if count > 5:  # Pattern appears in 5+ albums of this genre
                        genre_patterns.append(AdvancedPattern(
                            pattern_type='genre_pattern',
                            pattern_value=f"{genre}:{pattern}",
                            frequency=count,
                            affected_albums=[],
                            failure_rate=self.estimate_pattern_failure_rate(pattern),
                            complexity_correlation=avg_complexity,
                            recommended_action=f"Add genre-specific handling for {pattern} in {genre}",
                            priority='high' if avg_complexity > 0.6 else 'medium'
                        ))
                
                genre_insights[genre] = genre_patterns
        
        # 3. Statistical thresholds for auto-improvement
        character_threshold = self.calculate_optimal_threshold(self.character_frequency, total_albums)
        pattern_threshold = self.calculate_optimal_threshold(self.pattern_frequency, total_albums)
        
        # 4. Unicode script coverage analysis
        script_coverage = {}
        for combo, count in self.script_combinations.items():
            script_coverage[combo] = count
        
        # 5. Complexity distribution (detailed)
        complexity_ranges = {
            '0.0-0.2': 0, '0.2-0.4': 0, '0.4-0.6': 0, '0.6-0.8': 0, '0.8-1.0': 0
        }
        
        for album in albums:
            complexity = album['complexity_score']
            if complexity < 0.2:
                complexity_ranges['0.0-0.2'] += 1
            elif complexity < 0.4:
                complexity_ranges['0.2-0.4'] += 1
            elif complexity < 0.6:
                complexity_ranges['0.4-0.6'] += 1
            elif complexity < 0.8:
                complexity_ranges['0.6-0.8'] += 1
            else:
                complexity_ranges['0.8-1.0'] += 1
        
        # 6. Generate improvement recommendations
        recommendations = self.generate_improvement_recommendations(rare_chars, genre_insights)
        
        # 7. Auto-enhancement candidates (high-frequency, high-impact)
        auto_candidates = [
            pattern.recommended_action 
            for pattern in rare_chars 
            if pattern.frequency > 25 and pattern.priority == 'high'
        ]
        
        return LargeScaleInsights(
            total_albums_analyzed=total_albums,
            rare_characters_discovered=rare_chars,
            genre_specific_patterns=genre_insights,
            statistical_thresholds={
                'character_frequency_threshold': character_threshold,
                'pattern_frequency_threshold': pattern_threshold,
                'auto_improvement_threshold': 25,
                'critical_failure_rate': 0.1
            },
            unicode_script_coverage=script_coverage,
            complexity_distribution_detailed=complexity_ranges,
            improvement_recommendations=recommendations,
            auto_enhancement_candidates=auto_candidates
        )
    
    def estimate_failure_rate(self, char: str) -> float:
        """Estimate failure rate for a specific character"""
        
        # Unicode complexity categories with estimated failure rates
        char_code = ord(char)
        
        if 0x4E00 <= char_code <= 0x9FFF:  # CJK
            return 0.8  # High failure rate without proper mapping
        elif 0x0400 <= char_code <= 0x04FF:  # Cyrillic
            return 0.6  # Medium-high failure rate
        elif 0x0370 <= char_code <= 0x03FF:  # Greek
            return 0.5  # Medium failure rate
        elif char_code > 0xFF:  # Other high Unicode
            return 0.7  # High failure rate
        else:  # Latin extended
            return 0.3  # Lower failure rate
    
    def calculate_complexity_correlation(self, char: str, albums: List[Dict]) -> float:
        """Calculate correlation between character and album complexity"""
        
        char_complexities = []
        no_char_complexities = []
        
        for album in albums:
            full_text = f"{album['artist']} {album['album']}"
            if char in full_text:
                char_complexities.append(album['complexity_score'])
            else:
                no_char_complexities.append(album['complexity_score'])
        
        if not char_complexities or not no_char_complexities:
            return 0.0
        
        # Simple correlation: difference in means
        char_avg = statistics.mean(char_complexities)
        no_char_avg = statistics.mean(no_char_complexities)
        
        return char_avg - no_char_avg
    
    def estimate_pattern_failure_rate(self, pattern: str) -> float:
        """Estimate failure rate for a specific pattern"""
        
        # Pattern-based failure rate estimates
        failure_rates = {
            'has_cjk': 0.8,
            'very_long': 0.4,
            'extremely_long': 0.7,
            'punctuation_heavy': 0.5,
            'nested_parentheticals': 0.6,
            'consecutive_symbols': 0.7,
            'multiple_years': 0.3,
            'volume_series': 0.2,
            'multiple_editions': 0.4,
            'multiple_featured_artists': 0.3
        }
        
        return failure_rates.get(pattern, 0.1)
    
    def calculate_optimal_threshold(self, frequency_data: Counter, total_albums: int) -> float:
        """Calculate optimal threshold for pattern inclusion"""
        
        # Statistical approach: include patterns affecting >0.1% of albums
        min_frequency = max(1, total_albums * 0.001)  # 0.1% minimum
        
        # But also ensure we have at least 10 occurrences for reliability
        return max(min_frequency, 10)
    
    def generate_improvement_recommendations(self, rare_chars: List[AdvancedPattern], 
                                           genre_insights: Dict) -> List[str]:
        """Generate prioritized improvement recommendations"""
        
        recommendations = []
        
        # Character mapping recommendations
        high_freq_chars = [p for p in rare_chars if p.frequency > 50]
        if high_freq_chars:
            char_list = ', '.join([p.pattern_value for p in high_freq_chars[:10]])
            recommendations.append(f"HIGH PRIORITY: Add character mappings for: {char_list}")
        
        medium_freq_chars = [p for p in rare_chars if 25 <= p.frequency <= 50]
        if medium_freq_chars:
            char_list = ', '.join([p.pattern_value for p in medium_freq_chars[:10]])
            recommendations.append(f"MEDIUM PRIORITY: Add character mappings for: {char_list}")
        
        # Genre-specific recommendations
        for genre, patterns in genre_insights.items():
            high_impact_patterns = [p for p in patterns if p.frequency > 10]
            if high_impact_patterns:
                recommendations.append(f"GENRE-SPECIFIC: Enhance {genre} handling for patterns: {', '.join([p.pattern_value.split(':')[1] for p in high_impact_patterns[:3]])}")
        
        return recommendations

class LargeScaleReportGenerator:
    """Generates comprehensive reports from large-scale analysis"""
    
    def generate_comprehensive_report(self, insights: LargeScaleInsights) -> Dict:
        """Generate detailed report for 50K+ album analysis"""
        
        return {
            'analysis_metadata': {
                'analysis_date': datetime.now().isoformat(),
                'total_albums': insights.total_albums_analyzed,
                'analysis_type': 'large_scale_unicode_pattern_discovery'
            },
            'executive_summary': {
                'rare_characters_found': len(insights.rare_characters_discovered),
                'genre_patterns_identified': sum(len(patterns) for patterns in insights.genre_specific_patterns.values()),
                'auto_improvement_candidates': len(insights.auto_enhancement_candidates),
                'top_priority_improvements': len([r for r in insights.improvement_recommendations if 'HIGH PRIORITY' in r])
            },
            'character_analysis': {
                'rare_characters': [asdict(pattern) for pattern in insights.rare_characters_discovered],
                'frequency_distribution': self.analyze_frequency_distribution(insights.rare_characters_discovered),
                'script_coverage': insights.unicode_script_coverage
            },
            'genre_insights': {
                genre: [asdict(pattern) for pattern in patterns]
                for genre, patterns in insights.genre_specific_patterns.items()
            },
            'statistical_insights': {
                'thresholds': insights.statistical_thresholds,
                'complexity_distribution': insights.complexity_distribution_detailed,
                'coverage_gaps': self.identify_coverage_gaps(insights)
            },
            'actionable_improvements': {
                'immediate_actions': insights.auto_enhancement_candidates,
                'prioritized_recommendations': insights.improvement_recommendations,
                'implementation_roadmap': self.generate_implementation_roadmap(insights)
            }
        }
    
    def analyze_frequency_distribution(self, patterns: List[AdvancedPattern]) -> Dict:
        """Analyze frequency distribution of discovered patterns"""
        
        frequencies = [p.frequency for p in patterns]
        
        if not frequencies:
            return {'error': 'No patterns to analyze'}
        
        return {
            'total_patterns': len(frequencies),
            'mean_frequency': statistics.mean(frequencies),
            'median_frequency': statistics.median(frequencies),
            'max_frequency': max(frequencies),
            'frequency_ranges': {
                '1-10': len([f for f in frequencies if 1 <= f <= 10]),
                '11-25': len([f for f in frequencies if 11 <= f <= 25]),
                '26-50': len([f for f in frequencies if 26 <= f <= 50]),
                '51-100': len([f for f in frequencies if 51 <= f <= 100]),
                '100+': len([f for f in frequencies if f > 100])
            }
        }
    
    def identify_coverage_gaps(self, insights: LargeScaleInsights) -> List[str]:
        """Identify gaps in Unicode coverage at scale"""
        
        gaps = []
        
        # Script coverage gaps
        total_albums = insights.total_albums_analyzed
        for script, count in insights.unicode_script_coverage.items():
            coverage_percent = (count / total_albums) * 100
            if coverage_percent > 1.0:  # Scripts appearing in >1% of library
                gaps.append(f"Script coverage gap: {script} appears in {coverage_percent:.1f}% of albums")
        
        # Character frequency gaps
        high_freq_chars = [p for p in insights.rare_characters_discovered if p.frequency > 100]
        if high_freq_chars:
            gaps.append(f"High-frequency character gaps: {len(high_freq_chars)} characters each appear in 100+ albums")
        
        return gaps
    
    def generate_implementation_roadmap(self, insights: LargeScaleInsights) -> List[Dict]:
        """Generate step-by-step implementation roadmap"""
        
        roadmap = []
        
        # Phase 1: Critical character mappings
        critical_chars = [p for p in insights.rare_characters_discovered if p.priority == 'critical']
        if critical_chars:
            roadmap.append({
                'phase': 'Phase 1: Critical Character Mappings',
                'duration': '1-2 days',
                'actions': [p.recommended_action for p in critical_chars[:10]],
                'impact': f"Fixes {sum(p.frequency for p in critical_chars)} album search issues"
            })
        
        # Phase 2: High-frequency improvements
        high_priority = [p for p in insights.rare_characters_discovered if p.priority == 'high']
        if high_priority:
            roadmap.append({
                'phase': 'Phase 2: High-Frequency Improvements',
                'duration': '3-5 days',  
                'actions': [p.recommended_action for p in high_priority[:15]],
                'impact': f"Improves {sum(p.frequency for p in high_priority)} album searches"
            })
        
        # Phase 3: Genre-specific enhancements
        genre_actions = []
        for genre, patterns in insights.genre_specific_patterns.items():
            if len(patterns) > 3:
                genre_actions.append(f"Enhance {genre} pattern handling")
        
        if genre_actions:
            roadmap.append({
                'phase': 'Phase 3: Genre-Specific Enhancements',
                'duration': '1 week',
                'actions': genre_actions[:5],
                'impact': 'Improves genre-specific search accuracy'
            })
        
        return roadmap

async def main():
    """Main large-scale analysis workflow"""
    
    parser = argparse.ArgumentParser(description="Large-scale Unicode pattern analysis")
    parser.add_argument('--database', default='scripts/lidarr_library_analysis.db',
                       help='Library analysis database')
    parser.add_argument('--limit', type=int, default=50000,
                       help='Maximum albums to analyze')
    parser.add_argument('--comprehensive', action='store_true',
                       help='Include comprehensive rare pattern detection')
    parser.add_argument('--include-rare-patterns', action='store_true',
                       help='Include rare pattern analysis (slower)')
    parser.add_argument('--output', default='scripts/large_scale_analysis_results.json',
                       help='Output file for results')
    
    args = parser.parse_args()
    
    print("Large-Scale Unicode Pattern Analysis")
    print("=" * 50)
    print(f"Database: {args.database}")
    print(f"Target albums: {args.limit:,}")
    print(f"Comprehensive mode: {args.comprehensive}")
    print(f"Include rare patterns: {args.include_rare_patterns}")
    print("=" * 50)
    
    analyzer = LargeScaleAnalyzer()
    
    try:
        # Run large-scale analysis
        insights = await analyzer.analyze_large_library(
            args.database,
            limit=args.limit,
            include_rare_patterns=args.include_rare_patterns or args.comprehensive
        )
        
        # Generate comprehensive report
        report_generator = LargeScaleReportGenerator()
        report = report_generator.generate_comprehensive_report(insights)
        
        # Save results
        with open(args.output, 'w', encoding='utf-8') as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        
        # Display summary
        print("\nLARGE-SCALE ANALYSIS RESULTS")
        print("=" * 50)
        print(f"Albums analyzed: {insights.total_albums_analyzed:,}")
        print(f"Rare characters found: {len(insights.rare_characters_discovered)}")
        print(f"Genre patterns identified: {sum(len(p) for p in insights.genre_specific_patterns.values())}")
        print(f"Auto-improvement candidates: {len(insights.auto_enhancement_candidates)}")
        
        # Show top discoveries
        if insights.rare_characters_discovered:
            print(f"\nTOP CHARACTER DISCOVERIES:")
            for pattern in sorted(insights.rare_characters_discovered, key=lambda p: p.frequency, reverse=True)[:10]:
                print(f"   • '{pattern.pattern_value}' appears in {pattern.frequency} albums ({pattern.priority} priority)")
        
        # Show auto-improvements
        if insights.auto_enhancement_candidates:
            print(f"\nAUTO-IMPROVEMENT CANDIDATES:")
            for candidate in insights.auto_enhancement_candidates[:5]:
                print(f"   • {candidate}")
        
        # Show genre insights
        top_genres = sorted(insights.genre_specific_patterns.items(), 
                           key=lambda x: len(x[1]), reverse=True)[:5]
        if top_genres:
            print(f"\nGENRE-SPECIFIC PATTERNS:")
            for genre, patterns in top_genres:
                print(f"   • {genre}: {len(patterns)} patterns discovered")
        
        print(f"\nDetailed results saved to: {args.output}")
        print(f"\nNext steps:")
        print("1. Review auto-improvement candidates")
        print("2. Implement high-priority character mappings")
        print("3. Add genre-specific handling for discovered patterns")
        
    except Exception as e:
        logger.error(f"Large-scale analysis failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())