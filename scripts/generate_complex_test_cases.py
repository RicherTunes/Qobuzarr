#!/usr/bin/env python3
"""
Complex Test Case Generator

Generates comprehensive XUnit test cases from real Lidarr library gaps and
validated search results to ensure Unicode system covers all edge cases.

Usage:
    python scripts/generate_complex_test_cases.py
    python scripts/generate_complex_test_cases.py --validation-results scripts/gap_validation_results.json
    python scripts/generate_complex_test_cases.py --output tests/Qobuzarr.Tests/Unit/Indexers/LibraryDerivedTests.cs
"""

import json
import argparse
from pathlib import Path
from typing import List, Dict, Optional
import logging
from datetime import datetime

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class TestCaseGenerator:
    """Generates XUnit test cases from validated gap analysis results"""
    
    def __init__(self):
        pass
    
    def generate_xunit_test_class(self, validation_results: List[dict], 
                                 confirmed_gaps: List[dict]) -> str:
        """Generate complete XUnit test class with real-world test cases"""
        
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        
        test_class = f'''using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;

namespace Qobuzarr.Tests.Unit.Indexers
{{
    /// <summary>
    /// Library-derived test cases generated from real Lidarr library analysis.
    /// These tests ensure our Unicode query builder handles actual edge cases
    /// found in production usage rather than just theoretical examples.
    /// 
    /// Generated: {timestamp}
    /// Source: Real Lidarr library with {len(validation_results)} validated cases
    /// Confirmed gaps: {len(confirmed_gaps)} truly missing albums
    /// </summary>
    public class LibraryDerivedComplexTests
    {{
        private readonly UnicodeQueryBuilder _queryBuilder;

        public LibraryDerivedComplexTests()
        {{
            var logger = LogManager.GetCurrentClassLogger();
            _queryBuilder = new UnicodeQueryBuilder(logger);
        }}

        #region False Positive Corrections (Improve Unicode System)

'''
        
        # Add test cases for false positives (where manual search worked)
        false_positives = [r for r in validation_results if not r['gap_confirmed'] and r['working_queries']]
        
        for i, fp in enumerate(false_positives[:20], 1):  # Limit to top 20
            test_method = self.generate_false_positive_test_method(fp, i)
            test_class += test_method + "\n"
        
        test_class += '''
        #endregion

        #region Confirmed Gaps (Missing from Qobuz)

'''
        
        # Add test cases for confirmed gaps (document what we can't find)
        for i, gap in enumerate(confirmed_gaps[:10], 1):  # Limit to top 10
            test_method = self.generate_confirmed_gap_test_method(gap, i)
            test_class += test_method + "\n"
        
        test_class += '''
        #endregion

        #region Library Complexity Edge Cases

'''
        
        # Add edge cases from most complex albums
        complex_cases = sorted(validation_results, 
                             key=lambda x: x.get('complexity_score', 0), reverse=True)[:10]
        
        for i, case in enumerate(complex_cases, 1):
            test_method = self.generate_complexity_test_method(case, i)
            test_class += test_method + "\n"
        
        test_class += '''
        #endregion

        #region Character Pattern Tests

'''
        
        # Generate character pattern specific tests
        character_tests = self.generate_character_pattern_tests(validation_results)
        test_class += character_tests
        
        test_class += '''
        #endregion

        #region Test Data

        /// <summary>
        /// Real-world test cases derived from library analysis
        /// </summary>
        public static IEnumerable<object[]> GetLibraryDerivedTestCases()
        {
'''
        
        # Generate test data from all validation results
        for result in validation_results[:50]:  # Top 50 cases
            test_class += f'''            yield return new object[] {{
                "{self.escape_string(result['artist'])}",
                "{self.escape_string(result['album'])}", 
                {str(result['working_queries']).replace("'", '"')},
                "{result.get('best_working_query', '')}"
            }};
'''
        
        test_class += '''        }

        #endregion
    }
}'''
        
        return test_class
    
    def generate_false_positive_test_method(self, false_positive: dict, index: int) -> str:
        """Generate test method for false positive (improvement opportunity)"""
        
        artist = self.escape_string(false_positive['artist'])
        album = self.escape_string(false_positive['album'])
        working_query = self.escape_string(false_positive['best_working_query'])
        
        return f'''        [Fact]
        public void LibraryDerived_FalsePositive{index:02d}_ShouldGenerateWorkingVariant()
        {{
            // Arrange - Real case where Unicode system predicted failure but manual search worked
            var artist = "{artist}";
            var album = "{album}";
            var knownWorkingQuery = "{working_query}";

            // Act
            var variants = _queryBuilder.GenerateQueryVariants(artist, album);

            // Assert - Should generate variant similar to known working query
            variants.Should().Contain(v => 
                LevenshteinDistance(v.ToLowerInvariant(), knownWorkingQuery.ToLowerInvariant()) <= 2,
                $"Should generate variant close to known working query: '{{knownWorkingQuery}}'");
        }}'''
    
    def generate_confirmed_gap_test_method(self, confirmed_gap: dict, index: int) -> str:
        """Generate test method for confirmed gap (truly missing from Qobuz)"""
        
        artist = self.escape_string(confirmed_gap['artist'])
        album = self.escape_string(confirmed_gap['album'])
        
        return f'''        [Fact]
        public void LibraryDerived_ConfirmedGap{index:02d}_DocumentsMissingAlbum()
        {{
            // Arrange - Real case confirmed missing from Qobuz despite various search strategies
            var artist = "{artist}";
            var album = "{album}";

            // Act
            var variants = _queryBuilder.GenerateQueryVariants(artist, album);

            // Assert - Document that we generate reasonable variants even for missing albums
            variants.Should().NotBeEmpty("Should generate variants for missing albums");
            variants.Should().HaveCountGreaterOrEqualTo(2, "Should generate multiple fallback strategies");
            
            // Record that this album is confirmed missing from Qobuz catalog
            _queryBuilder.RecordVariantResult($"{{artist}} {{album}}", variants[0], false, 0);
        }}'''
    
    def generate_complexity_test_method(self, complex_case: dict, index: int) -> str:
        """Generate test method for high complexity cases"""
        
        artist = self.escape_string(complex_case['artist'])
        album = self.escape_string(complex_case['album'])
        complexity = complex_case.get('complexity_score', 0)
        
        return f'''        [Fact]
        public void LibraryDerived_HighComplexity{index:02d}_HandlesGracefully()
        {{
            // Arrange - High complexity case from real library (score: {complexity:.3f})
            var artist = "{artist}";
            var album = "{album}";

            // Act
            var variants = _queryBuilder.GenerateQueryVariants(artist, album);

            // Assert - Should handle high complexity gracefully
            variants.Should().NotBeEmpty("Should generate variants for high complexity albums");
            
            // Should have reasonable performance even for complex cases
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {{
                _queryBuilder.GenerateQueryVariants(artist, album);
            }}
            stopwatch.Stop();
            
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, 
                "Complex cases should still complete quickly");
        }}'''
    
    def generate_character_pattern_tests(self, validation_results: List[dict]) -> str:
        """Generate specific tests for character patterns found in library"""
        
        # Analyze character patterns in the validation results
        unicode_scripts = set()
        unsupported_chars = set()
        
        for result in validation_results:
            full_text = f"{result['artist']} {result['album']}"
            for char in full_text:
                if ord(char) > 127:
                    try:
                        script = unicodedata.name(char).split()[0]
                        unicode_scripts.add(script)
                    except:
                        unsupported_chars.add(char)
        
        tests = ""
        
        # Generate script-specific tests
        if 'CYRILLIC' in unicode_scripts:
            tests += '''        [Fact]
        public void LibraryDerived_CyrillicContent_TransliteratesCorrectly()
        {
            // Test cases derived from actual Cyrillic content in library
            var cyrillicCases = GetCyrillicCasesFromLibrary();
            
            foreach (var (artist, album) in cyrillicCases)
            {
                var variants = _queryBuilder.GenerateQueryVariants(artist, album);
                variants.Should().Contain(v => v.All(c => c <= 127), 
                    $"Should generate ASCII variant for Cyrillic: {artist} - {album}");
            }
        }

'''
        
        if 'GREEK' in unicode_scripts:
            tests += '''        [Fact]
        public void LibraryDerived_GreekContent_TransliteratesCorrectly()
        {
            // Test cases derived from actual Greek content in library  
            var greekCases = GetGreekCasesFromLibrary();
            
            foreach (var (artist, album) in greekCases)
            {
                var variants = _queryBuilder.GenerateQueryVariants(artist, album);
                variants.Should().Contain(v => !v.Contains("μ") || v.Contains("m"), 
                    $"Should transliterate Greek characters: {artist} - {album}");
            }
        }

'''
        
        return tests
    
    def escape_string(self, text: str) -> str:
        """Escape string for C# code generation"""
        if not text:
            return ""
        return text.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r')
    
    def generate_helper_methods(self, validation_results: List[dict]) -> str:
        """Generate helper methods for the test class"""
        
        # Extract Cyrillic and Greek cases from results
        cyrillic_cases = []
        greek_cases = []
        
        for result in validation_results:
            full_text = f"{result['artist']} {result['album']}"
            has_cyrillic = any(0x0400 <= ord(c) <= 0x04FF for c in full_text)
            has_greek = any(0x0370 <= ord(c) <= 0x03FF for c in full_text)
            
            if has_cyrillic:
                cyrillic_cases.append((result['artist'], result['album']))
            if has_greek:
                greek_cases.append((result['artist'], result['album']))
        
        helpers = '''
        #region Helper Methods

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            int[,] d = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= target.Length; j++) d[0, j] = j;

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[source.Length, target.Length];
        }

        /// <summary>
        /// Get Cyrillic test cases from library analysis
        /// </summary>
        private IEnumerable<(string artist, string album)> GetCyrillicCasesFromLibrary()
        {
'''
        
        for artist, album in cyrillic_cases[:5]:  # Top 5 Cyrillic cases
            helpers += f'            yield return ("{self.escape_string(artist)}", "{self.escape_string(album)}");\n'
        
        helpers += '''        }

        /// <summary>
        /// Get Greek test cases from library analysis  
        /// </summary>
        private IEnumerable<(string artist, string album)> GetGreekCasesFromLibrary()
        {
'''
        
        for artist, album in greek_cases[:5]:  # Top 5 Greek cases
            helpers += f'            yield return ("{self.escape_string(artist)}", "{self.escape_string(album)}");\n'
        
        helpers += '''        }

        #endregion'''
        
        return helpers

async def main():
    """Main test case generation workflow"""
    parser = argparse.ArgumentParser(description="Generate complex test cases from library analysis")
    parser.add_argument('--validation-results', default='scripts/gap_validation_results.json',
                       help='Gap validation results file')
    parser.add_argument('--gaps-analysis', default='scripts/unicode_gaps_analysis.json',
                       help='Original gaps analysis file (for confirmed gaps)')
    parser.add_argument('--output', default='tests/Qobuzarr.Tests/Unit/Indexers/LibraryDerivedComplexTests.cs',
                       help='Output file for generated test class')
    parser.add_argument('--max-test-cases', type=int, default=50,
                       help='Maximum number of test cases to generate')
    
    args = parser.parse_args()
    
    print("🧪 Complex Test Case Generation")
    print("=" * 50)
    print(f"📊 Validation results: {args.validation_results}")
    print(f"📈 Gaps analysis: {args.gaps_analysis}")
    print(f"💾 Output: {args.output}")
    print("=" * 50)
    
    generator = TestCaseGenerator()
    
    try:
        # Load validation results
        validation_results = []
        if Path(args.validation_results).exists():
            with open(args.validation_results, 'r', encoding='utf-8') as f:
                data = json.load(f)
                validation_results = data.get('validation_results', [])
        
        # Load gaps analysis for confirmed gaps
        confirmed_gaps = []
        if Path(args.gaps_analysis).exists():
            with open(args.gaps_analysis, 'r', encoding='utf-8') as f:
                data = json.load(f)
                confirmed_gaps = data.get('gaps', [])
        
        if not validation_results and not confirmed_gaps:
            print("❌ No validation results or gaps found")
            print("First run:")
            print("1. python scripts/extract_lidarr_library.py")
            print("2. python scripts/analyze_unicode_gaps.py") 
            print("3. python scripts/validate_qobuz_gaps.py")
            return
        
        logger.info(f"📖 Loaded {len(validation_results)} validation results")
        logger.info(f"📖 Loaded {len(confirmed_gaps)} gap analyses")
        
        # Generate test class
        test_class_code = generator.generate_xunit_test_class(
            validation_results[:args.max_test_cases], 
            confirmed_gaps[:args.max_test_cases]
        )
        
        # Add helper methods
        helper_methods = generator.generate_helper_methods(validation_results)
        test_class_code = test_class_code.replace('#endregion\n    }\n}', f'{helper_methods}\n    }}\n}}')
        
        # Ensure output directory exists
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        
        # Write test class
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(test_class_code)
        
        # Generate statistics
        false_positives = [r for r in validation_results if not r['gap_confirmed']]
        confirmed_gaps_from_validation = [r for r in validation_results if r['gap_confirmed']]
        
        print(f"\n🧪 TEST GENERATION COMPLETE")
        print("=" * 50)
        print(f"📁 Generated: {output_path}")
        print(f"📊 Test cases created:")
        print(f"   • False positive corrections: {len(false_positives[:20])}")
        print(f"   • Confirmed gap documentation: {len(confirmed_gaps[:10])}")
        print(f"   • Complexity edge cases: {min(10, len(validation_results))}")
        print(f"   • Character pattern tests: Generated")
        
        # Show improvement opportunities
        if false_positives:
            print(f"\n🎯 UNICODE SYSTEM IMPROVEMENTS IDENTIFIED:")
            working_strategies = {}
            for fp in false_positives:
                for result in fp.get('search_results', []):
                    if result.get('found', False):
                        strategy = result.get('search_strategy', 'unknown')
                        working_strategies[strategy] = working_strategies.get(strategy, 0) + 1
            
            for strategy, count in sorted(working_strategies.items(), key=lambda x: x[1], reverse=True)[:5]:
                print(f"   • {strategy}: worked for {count} albums")
        
        print(f"\n📈 IMPACT ANALYSIS:")
        total_library_complexity = len(validation_results)
        unicode_system_success_rate = (len(validation_results) - len(confirmed_gaps_from_validation)) / len(validation_results) if validation_results else 1.0
        print(f"   • Unicode system success rate: {unicode_system_success_rate:.1%}")
        print(f"   • Complex albums in library: {total_library_complexity}")
        print(f"   • Truly missing from Qobuz: {len(confirmed_gaps_from_validation)}")
        
        print(f"\n🎯 Next steps:")
        print("1. Run the generated tests: dotnet test")
        print("2. Implement improvements for false positive patterns")
        print("3. Monitor Unicode system with production telemetry")
        
    except FileNotFoundError as e:
        print(f"❌ Error: {e}")
        print("Ensure you have run the gap analysis pipeline first")
    except Exception as e:
        logger.error(f"💥 Test generation failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())