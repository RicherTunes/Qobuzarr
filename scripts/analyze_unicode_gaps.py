#!/usr/bin/env python3
"""
Unicode System Gap Analyzer

Tests the Unicode query builder against real Lidarr library data to identify
albums that might still be missed despite our comprehensive Unicode handling.

Usage:
    python scripts/analyze_unicode_gaps.py
    python scripts/analyze_unicode_gaps.py --database scripts/lidarr_library_analysis.db
    python scripts/analyze_unicode_gaps.py --test-sample 100 --output gaps_analysis.json
"""

import sqlite3
import json
import unicodedata
import re
import argparse
from dataclasses import dataclass, asdict
from typing import List, Dict, Optional, Tuple
from pathlib import Path
import logging

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

@dataclass
class SearchGap:
    """Represents a potential gap in our Unicode search system"""
    artist: str
    album: str
    lidarr_id: int
    complexity_score: float
    complexity_factors: List[str]
    generated_variants: List[str]
    predicted_failure_reason: str
    gap_severity: str  # 'critical', 'high', 'medium', 'low'
    character_analysis: Dict[str, any]
    recommended_fixes: List[str]

class UnicodeQuerySimulator:
    """Simulates our C# UnicodeQueryBuilder logic in Python"""
    
    # Character mappings from our C# implementation
    CHARACTER_VARIANTS = {
        'á': 'a', 'é': 'e', 'í': 'i', 'ó': 'o', 'ú': 'u', 'ñ': 'n', 'ç': 'c', 'ß': 'ss',
        'ø': 'o', 'å': 'a', 'æ': 'ae', 'ż': 'z', 'ł': 'l', 'š': 's', 'č': 'c', 'ř': 'r',
        'þ': 'th', 'ð': 'd',
        # Greek characters
        'α': 'a', 'β': 'b', 'γ': 'g', 'δ': 'd', 'ε': 'e', 'μ': 'm', 'π': 'p', 'σ': 's',
        # Cyrillic (basic set)
        'а': 'a', 'б': 'b', 'в': 'v', 'г': 'g', 'д': 'd', 'е': 'e', 'ж': 'zh', 'з': 'z',
        'и': 'i', 'к': 'k', 'л': 'l', 'м': 'm', 'н': 'n', 'о': 'o', 'п': 'p', 'р': 'r',
        'с': 's', 'т': 't', 'у': 'u', 'ф': 'f', 'х': 'kh', 'ц': 'ts', 'ч': 'ch',
        'ш': 'sh', 'щ': 'shch', 'ы': 'y', 'э': 'e', 'ю': 'yu', 'я': 'ya'
    }
    
    KNOWN_CORRECTIONS = {
        'sigur rós': 'sigur ros',
        'björk': 'bjork',
        'mötley crüe': 'motley crue',
        'blue öyster cult': 'blue oyster cult',
        'hüsker dü': 'husker du',
        'motörhead': 'motorhead',
        'café tacvba': 'cafe tacvba',
        'röyksopp': 'royksopp',
        'trentemøller': 'trentemoller',
        'μ-ziq': 'mu-ziq'
    }
    
    def simulate_generate_query_variants(self, artist: str, album: str, max_variants: int = 6) -> List[str]:
        """Simulate our C# UnicodeQueryBuilder.GenerateQueryVariants logic"""
        
        if not artist or not album:
            return []
        
        variants = []
        full_query = f"{artist.strip()} {album.strip()}"
        
        # 1. Original query
        variants.append(full_query)
        
        # 2. ASCII folding
        ascii_folded = self.fold_to_ascii(full_query)
        if ascii_folded != full_query:
            variants.append(ascii_folded)
        
        # 3. Known corrections
        corrected = self.apply_known_corrections(full_query)
        if corrected != full_query:
            variants.append(corrected)
        
        # 4. Greek transliteration
        greek_transliterated = self.transliterate_greek(full_query)
        if greek_transliterated != full_query:
            variants.append(greek_transliterated)
        
        # 5. Cyrillic transliteration
        cyrillic_transliterated = self.transliterate_cyrillic(full_query)
        if cyrillic_transliterated != full_query:
            variants.append(cyrillic_transliterated)
        
        # 6. Component searches
        variants.append(self.fold_to_ascii(artist.strip()))
        variants.append(self.fold_to_ascii(album.strip()))
        
        # 7. Remove special characters (nuclear option)
        alphanumeric = re.sub(r'[^\w\s]', ' ', full_query)
        alphanumeric = re.sub(r'\s+', ' ', alphanumeric).strip()
        if alphanumeric != full_query and alphanumeric:
            variants.append(alphanumeric)
        
        # Remove duplicates and limit
        unique_variants = list(dict.fromkeys(variants))
        return unique_variants[:max_variants]
    
    def fold_to_ascii(self, text: str) -> str:
        """ASCII folding - remove diacritics"""
        if not text:
            return text
            
        result = []
        for char in text:
            lower_char = char.lower()
            if lower_char in self.CHARACTER_VARIANTS:
                result.append(self.CHARACTER_VARIANTS[lower_char])
            else:
                # Use Unicode normalization for other characters
                normalized = unicodedata.normalize('NFD', char)
                ascii_char = ''.join(c for c in normalized if unicodedata.category(c) != 'Mn')
                result.append(ascii_char or char)
        
        return ''.join(result)
    
    def apply_known_corrections(self, query: str) -> str:
        """Apply known artist corrections"""
        query_lower = query.lower()
        
        for original, correction in self.KNOWN_CORRECTIONS.items():
            if original in query_lower:
                return query_lower.replace(original, correction)
        
        return query
    
    def transliterate_greek(self, text: str) -> str:
        """Transliterate Greek characters"""
        # Simplified Greek transliteration
        greek_map = {'μ': 'm', 'α': 'a', 'β': 'b', 'γ': 'g', 'δ': 'd', 'ε': 'e'}
        
        result = text
        for greek, latin in greek_map.items():
            result = result.replace(greek, latin)
        
        return result
    
    def transliterate_cyrillic(self, text: str) -> str:
        """Transliterate Cyrillic characters"""
        result = []
        for char in text:
            if char.lower() in self.CHARACTER_VARIANTS and any(
                ord(c) >= 0x0400 and ord(c) <= 0x04FF for c in [char]):
                result.append(self.CHARACTER_VARIANTS[char.lower()])
            else:
                result.append(char)
        
        return ''.join(result)
    
    def requires_unicode_handling(self, query: str) -> bool:
        """Check if query requires Unicode handling"""
        return any(ord(c) > 127 for c in query)

class GapAnalyzer:
    """Analyzes potential gaps in Unicode system coverage"""
    
    def __init__(self):
        self.unicode_simulator = UnicodeQuerySimulator()
        
    def analyze_library_for_gaps(self, albums: List[dict], 
                                min_complexity: float = 0.4) -> List[SearchGap]:
        """Analyze library albums for potential Unicode system gaps"""
        
        logger.info(f"🔍 Analyzing {len(albums)} albums for Unicode gaps")
        logger.info(f"📊 Minimum complexity: {min_complexity}")
        
        gaps = []
        analyzed_count = 0
        
        for album_data in albums:
            try:
                if album_data['complexity_score'] < min_complexity:
                    continue
                
                analyzed_count += 1
                
                # Generate variants using our Unicode system simulation
                variants = self.unicode_simulator.simulate_generate_query_variants(
                    album_data['artist'], album_data['album']
                )
                
                # Analyze if these variants are likely to succeed
                gap = self.predict_search_gap(album_data, variants)
                
                if gap:
                    gaps.append(gap)
                    
                if analyzed_count % 100 == 0:
                    logger.info(f"📈 Progress: {analyzed_count} analyzed, {len(gaps)} gaps found")
                    
            except Exception as e:
                logger.warning(f"⚠️ Error analyzing album {album_data.get('artist', 'Unknown')}: {e}")
                continue
        
        logger.info(f"🏁 Analysis complete: {len(gaps)} potential gaps found from {analyzed_count} albums")
        return gaps
    
    def predict_search_gap(self, album_data: dict, variants: List[str]) -> Optional[SearchGap]:
        """Predict if the generated variants are likely to fail in Qobuz search"""
        
        artist = album_data['artist']
        album = album_data['album']
        
        # Analyze potential failure reasons
        failure_reasons = []
        severity = 'low'
        
        # Check for character combinations not well handled
        char_analysis = self.analyze_character_patterns(artist, album)
        
        # 1. Unsupported character combinations
        if char_analysis['has_unsupported_chars']:
            failure_reasons.append("unsupported_unicode_chars")
            severity = 'high'
        
        # 2. Complex nested punctuation
        if char_analysis['nested_punctuation_depth'] > 2:
            failure_reasons.append("complex_nested_punctuation")
            severity = max(severity, 'medium')
        
        # 3. Multiple edition indicators
        if char_analysis['edition_indicator_count'] > 2:
            failure_reasons.append("excessive_edition_text")
            severity = max(severity, 'medium')
        
        # 4. Very long names (>100 chars)
        if len(f"{artist} {album}") > 100:
            failure_reasons.append("excessive_length")
            severity = max(severity, 'medium')
        
        # 5. Mixed scripts without proper handling
        if len(char_analysis['unicode_scripts']) > 2:
            failure_reasons.append("multiple_script_mixing")
            severity = 'high'
        
        # 6. No good ASCII fallback
        ascii_variants = [v for v in variants if all(ord(c) <= 127 for c in v)]
        if not ascii_variants or all(len(v.split()) < 2 for v in ascii_variants):
            failure_reasons.append("poor_ascii_fallback")
            severity = 'critical'
        
        # Only flag as gap if we predict failure
        if failure_reasons:
            recommended_fixes = self.generate_fix_recommendations(failure_reasons, char_analysis)
            
            return SearchGap(
                artist=artist,
                album=album,
                lidarr_id=album_data['lidarr_id'],
                complexity_score=album_data['complexity_score'],
                complexity_factors=json.loads(album_data['complexity_factors']),
                generated_variants=variants,
                predicted_failure_reason='; '.join(failure_reasons),
                gap_severity=severity,
                character_analysis=char_analysis,
                recommended_fixes=recommended_fixes
            )
        
        return None
    
    def analyze_character_patterns(self, artist: str, album: str) -> Dict[str, any]:
        """Detailed character pattern analysis"""
        
        full_text = f"{artist} {album}"
        
        # Detect Unicode scripts
        scripts = set()
        unsupported_chars = []
        
        for char in full_text:
            if ord(char) > 127:
                try:
                    script_name = unicodedata.name(char).split()[0]
                    scripts.add(script_name)
                    
                    # Check if we support this character
                    if (char.lower() not in self.unicode_simulator.CHARACTER_VARIANTS and
                        not self.is_standard_latin_extended(char)):
                        unsupported_chars.append(char)
                        
                except ValueError:
                    unsupported_chars.append(char)
        
        # Analyze punctuation patterns
        nested_depth = self.calculate_nested_punctuation_depth(album)
        
        # Count edition indicators
        edition_terms = ['deluxe', 'remaster', 'anniversary', 'special', 'expanded', 
                        'collector', 'limited', 'bonus', 'extended']
        edition_count = sum(1 for term in edition_terms if term in album.lower())
        
        # Detect problematic patterns
        problematic_patterns = []
        
        # Multiple consecutive special chars
        if re.search(r'[^\w\s]{3,}', full_text):
            problematic_patterns.append('consecutive_special_chars')
        
        # Multiple years
        years = re.findall(r'\b(19|20)\d{2}\b', full_text)
        if len(years) > 1:
            problematic_patterns.append('multiple_years')
        
        # Nested parentheticals
        if re.search(r'\([^)]*\([^)]*\)[^)]*\)', album):
            problematic_patterns.append('nested_parentheticals')
        
        # Featured artist chaos
        feat_count = len(re.findall(r'\b(feat|ft|featuring)\.?\s+', full_text, re.IGNORECASE))
        if feat_count > 1:
            problematic_patterns.append('multiple_featured_artists')
        
        return {
            'unicode_scripts': list(scripts),
            'unsupported_chars': unsupported_chars,
            'has_unsupported_chars': len(unsupported_chars) > 0,
            'nested_punctuation_depth': nested_depth,
            'edition_indicator_count': edition_count,
            'problematic_patterns': problematic_patterns,
            'total_special_chars': sum(1 for c in full_text if not c.isalnum() and not c.isspace()),
            'ascii_ratio': sum(1 for c in full_text if ord(c) <= 127) / len(full_text)
        }
    
    def calculate_nested_punctuation_depth(self, text: str) -> int:
        """Calculate maximum nesting depth of parentheticals"""
        depth = 0
        max_depth = 0
        
        for char in text:
            if char in '([':
                depth += 1
                max_depth = max(max_depth, depth)
            elif char in ')]':
                depth = max(0, depth - 1)
        
        return max_depth
    
    def is_standard_latin_extended(self, char: str) -> bool:
        """Check if character is in standard Latin extended range"""
        code = ord(char)
        return (0x0100 <= code <= 0x017F) or (0x1E00 <= code <= 0x1EFF)
    
    def generate_fix_recommendations(self, failure_reasons: List[str], 
                                   char_analysis: Dict) -> List[str]:
        """Generate specific recommendations to fix identified gaps"""
        
        fixes = []
        
        if 'unsupported_unicode_chars' in failure_reasons:
            unsupported = char_analysis['unsupported_chars']
            fixes.append(f"Add character mappings: {', '.join(set(unsupported))}")
        
        if 'complex_nested_punctuation' in failure_reasons:
            fixes.append("Enhance parenthetical removal to handle nested structures")
        
        if 'excessive_edition_text' in failure_reasons:
            fixes.append("Improve special edition text detection and removal")
        
        if 'multiple_script_mixing' in failure_reasons:
            scripts = char_analysis['unicode_scripts']
            fixes.append(f"Add mixed-script handling for: {', '.join(scripts)}")
        
        if 'poor_ascii_fallback' in failure_reasons:
            fixes.append("Improve ASCII fallback generation strategy")
        
        if 'excessive_length' in failure_reasons:
            fixes.append("Add intelligent text truncation for very long names")
        
        return fixes

class LibraryGapAnalyzer:
    """Main analyzer that processes library data and identifies gaps"""
    
    def __init__(self, database_path: str = "scripts/lidarr_library_analysis.db"):
        self.database_path = database_path
        self.gap_analyzer = GapAnalyzer()
        
    def load_library_albums(self, limit: Optional[int] = None, 
                           min_complexity: float = 0.4) -> List[dict]:
        """Load albums from database with complexity filtering"""
        
        if not Path(self.database_path).exists():
            raise FileNotFoundError(f"Database not found: {self.database_path}")
        
        conn = sqlite3.connect(self.database_path)
        cursor = conn.cursor()
        
        # Query for complex albums
        query = '''
            SELECT * FROM library_albums 
            WHERE complexity_score >= ? 
            ORDER BY complexity_score DESC
        '''
        
        if limit:
            query += f' LIMIT {limit}'
        
        cursor.execute(query, (min_complexity,))
        columns = [description[0] for description in cursor.description]
        
        albums = []
        for row in cursor.fetchall():
            album_dict = dict(zip(columns, row))
            albums.append(album_dict)
        
        conn.close()
        
        logger.info(f"📖 Loaded {len(albums)} complex albums from library")
        return albums
    
    def analyze_gaps(self, test_sample: Optional[int] = None, 
                    min_complexity: float = 0.4) -> List[SearchGap]:
        """Main gap analysis workflow"""
        
        # Load library data
        albums = self.load_library_albums(limit=test_sample, min_complexity=min_complexity)
        
        if not albums:
            logger.warning("❌ No albums found for analysis")
            return []
        
        # Analyze for gaps
        gaps = self.gap_analyzer.analyze_library_for_gaps(albums, min_complexity)
        
        # Sort by severity and complexity
        gaps.sort(key=lambda g: (
            {'critical': 4, 'high': 3, 'medium': 2, 'low': 1}[g.gap_severity],
            g.complexity_score
        ), reverse=True)
        
        return gaps
    
    def generate_gap_report(self, gaps: List[SearchGap]) -> Dict:
        """Generate comprehensive gap analysis report"""
        
        if not gaps:
            return {
                'summary': 'No gaps detected - Unicode system covers all analyzed albums!',
                'total_gaps': 0
            }
        
        # Categorize gaps by severity
        severity_counts = {}
        for gap in gaps:
            severity_counts[gap.gap_severity] = severity_counts.get(gap.gap_severity, 0) + 1
        
        # Analyze failure patterns
        failure_reasons = {}
        for gap in gaps:
            for reason in gap.predicted_failure_reason.split('; '):
                failure_reasons[reason] = failure_reasons.get(reason, 0) + 1
        
        # Most complex gaps
        top_gaps = gaps[:10]
        
        # Character analysis
        all_unsupported_chars = set()
        for gap in gaps:
            all_unsupported_chars.update(gap.character_analysis['unsupported_chars'])
        
        return {
            'analysis_date': datetime.now().isoformat(),
            'total_gaps': len(gaps),
            'severity_distribution': severity_counts,
            'failure_patterns': dict(sorted(failure_reasons.items(), key=lambda x: x[1], reverse=True)),
            'unsupported_characters': list(all_unsupported_chars),
            'top_10_most_complex_gaps': [
                {
                    'artist': gap.artist,
                    'album': gap.album,
                    'complexity_score': gap.complexity_score,
                    'gap_severity': gap.gap_severity,
                    'failure_reason': gap.predicted_failure_reason,
                    'generated_variants': gap.generated_variants,
                    'recommended_fixes': gap.recommended_fixes
                }
                for gap in top_gaps
            ],
            'recommended_unicode_enhancements': self.generate_enhancement_recommendations(gaps)
        }
    
    def generate_enhancement_recommendations(self, gaps: List[SearchGap]) -> List[str]:
        """Generate prioritized enhancement recommendations"""
        
        recommendations = []
        
        # Count fix types
        fix_counts = {}
        for gap in gaps:
            for fix in gap.recommended_fixes:
                fix_counts[fix] = fix_counts.get(fix, 0) + 1
        
        # Generate recommendations based on frequency
        for fix, count in sorted(fix_counts.items(), key=lambda x: x[1], reverse=True):
            if count >= 3:  # Only recommend fixes needed for 3+ albums
                recommendations.append(f"{fix} (affects {count} albums)")
        
        return recommendations

async def main():
    """Main gap analysis workflow"""
    parser = argparse.ArgumentParser(description="Analyze Unicode system gaps against Lidarr library")
    parser.add_argument('--database', default='scripts/lidarr_library_analysis.db',
                       help='Path to library analysis database')
    parser.add_argument('--test-sample', type=int, default=None,
                       help='Limit analysis to N most complex albums')
    parser.add_argument('--min-complexity', type=float, default=0.4,
                       help='Minimum complexity score to analyze')
    parser.add_argument('--output', default='scripts/unicode_gaps_analysis.json',
                       help='Output file for gap analysis results')
    
    args = parser.parse_args()
    
    print("🔍 Unicode System Gap Analysis")
    print("=" * 50)
    print(f"📊 Database: {args.database}")
    print(f"🎯 Sample size: {args.test_sample or 'All albums'}")
    print(f"📈 Min complexity: {args.min_complexity}")
    print("=" * 50)
    
    analyzer = LibraryGapAnalyzer(args.database)
    
    try:
        # Run gap analysis
        gaps = analyzer.analyze_gaps(
            test_sample=args.test_sample,
            min_complexity=args.min_complexity
        )
        
        # Generate report
        report = analyzer.generate_gap_report(gaps)
        
        # Save results
        with open(args.output, 'w', encoding='utf-8') as f:
            json.dump({
                'gaps': [asdict(gap) for gap in gaps],
                'report': report
            }, f, indent=2, ensure_ascii=False)
        
        # Display summary
        print(f"\n📊 GAP ANALYSIS RESULTS")
        print("=" * 50)
        
        if gaps:
            print(f"🔍 Total gaps found: {len(gaps)}")
            print(f"📈 Severity distribution: {report['severity_distribution']}")
            print(f"🔧 Top failure patterns:")
            for pattern, count in list(report['failure_patterns'].items())[:5]:
                print(f"   • {pattern}: {count} albums")
            
            if report['unsupported_characters']:
                print(f"🌍 Unsupported characters: {', '.join(report['unsupported_characters'])}")
            
            print(f"\n🎯 PRIORITY ENHANCEMENTS:")
            for rec in report['recommended_unicode_enhancements'][:5]:
                print(f"   • {rec}")
            
            print(f"\n🏆 TOP 3 MOST COMPLEX GAPS:")
            for gap in report['top_10_most_complex_gaps'][:3]:
                print(f"   • {gap['artist']} - {gap['album']}")
                print(f"     Score: {gap['complexity_score']:.3f} | Reason: {gap['failure_reason']}")
        else:
            print("🎉 NO GAPS FOUND!")
            print("Your Unicode system appears to handle all complex albums in your library!")
        
        print(f"\n💾 Detailed results saved to: {args.output}")
        
        if gaps:
            print(f"\n🎯 Next steps:")
            print("1. Run: python scripts/validate_qobuz_gaps.py")
            print("2. Manual validation of predicted gaps against real Qobuz search")
            print("3. Generate enhanced test cases from confirmed gaps")
        
    except FileNotFoundError as e:
        print(f"❌ Error: {e}")
        print("First run: python scripts/extract_lidarr_library.py")
    except Exception as e:
        logger.error(f"💥 Analysis failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())