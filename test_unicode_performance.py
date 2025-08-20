#!/usr/bin/env python3
"""
Performance validation script for Unicode query builder implementation.
Tests the effectiveness of our deterministic Unicode handling approach.
"""

import json
import time
from typing import List, Dict, Tuple

class UnicodeQueryTester:
    """Python implementation for testing our C# Unicode query logic"""
    
    def __init__(self):
        # Load our test cases from the JSON mapping
        with open('src/Data/UnicodeMapping.json', 'r', encoding='utf-8') as f:
            self.mapping_data = json.load(f)
        
        self.test_results = []
    
    def simulate_unicode_query_generation(self, artist: str, album: str) -> List[str]:
        """Simulate our C# UnicodeQueryBuilder.GenerateQueryVariants logic"""
        variants = []
        full_query = f"{artist.strip()} {album.strip()}"
        
        # 1. Original query
        variants.append(full_query)
        
        # 2. ASCII folding (é→e, ñ→n, etc.)
        ascii_folded = self.fold_to_ascii(full_query)
        if ascii_folded != full_query:
            variants.append(ascii_folded)
        
        # 3. Known artist corrections
        corrected = self.apply_known_corrections(full_query)
        if corrected != full_query:
            variants.append(corrected)
        
        # 4. Cyrillic transliteration
        transliterated = self.transliterate_cyrillic(full_query)
        if transliterated != full_query:
            variants.append(transliterated)
        
        # 5. Component searches
        variants.append(self.fold_to_ascii(artist.strip()))
        variants.append(self.fold_to_ascii(album.strip()))
        
        # Remove duplicates and limit to 6
        unique_variants = list(dict.fromkeys(variants))[:6]
        return unique_variants
    
    def fold_to_ascii(self, text: str) -> str:
        """ASCII folding - remove diacritics"""
        import unicodedata
        # Normalize to decomposed form, remove diacritics, recompose
        normalized = unicodedata.normalize('NFD', text)
        ascii_text = ''.join(c for c in normalized if unicodedata.category(c) != 'Mn')
        return unicodedata.normalize('NFC', ascii_text)
    
    def apply_known_corrections(self, query: str) -> str:
        """Apply known artist corrections"""
        query_lower = query.lower()
        
        # Check all correction categories
        for category in ['nordic', 'german', 'french', 'spanish', 'portuguese']:
            corrections = self.mapping_data['artist_corrections'].get(category, {})
            for original, alternatives in corrections.items():
                if original.lower() in query_lower:
                    # Use first alternative
                    return query_lower.replace(original.lower(), alternatives[0].lower())
        
        return query
    
    def transliterate_cyrillic(self, text: str) -> str:
        """Transliterate Cyrillic characters"""
        cyrillic_map = self.mapping_data['transliteration_rules']['cyrillic_to_latin']['character_map']
        
        result = ""
        for char in text:
            result += cyrillic_map.get(char, cyrillic_map.get(char.lower(), char))
        
        return result
    
    def requires_unicode_handling(self, query: str) -> bool:
        """Check if query needs Unicode handling"""
        return any(ord(c) > 127 for c in query)
    
    def run_performance_test(self) -> Dict[str, any]:
        """Run comprehensive performance test with real-world cases"""
        
        # Load test cases
        test_cases = self.mapping_data['test_cases']['known_difficult']
        edge_cases = self.mapping_data['test_cases']['edge_cases']
        all_cases = test_cases + edge_cases
        
        print(f"Testing Unicode Query Performance with {len(all_cases)} cases")
        print("=" * 60)
        
        start_time = time.time()
        
        successful_generations = 0
        unicode_cases = 0
        ascii_cases = 0
        
        for i, case in enumerate(all_cases):
            artist = case['artist']
            album = case['album']
            
            try:
                # Generate variants
                variants = self.simulate_unicode_query_generation(artist, album)
                
                # Check if Unicode handling was needed
                if self.requires_unicode_handling(f"{artist} {album}"):
                    unicode_cases += 1
                else:
                    ascii_cases += 1
                
                # Validate expected variants are present
                expected_variants = case.get('expected_variants', [])
                has_expected = all(
                    any(expected.lower() in variant.lower() for variant in variants)
                    for expected in expected_variants
                )
                
                if variants and len(variants) >= 2 and has_expected:
                    successful_generations += 1
                    status = "PASS"
                else:
                    status = "FAIL"
                
                print(f"{i+1:2d}. {artist} - {album}")
                print(f"    {status} | Variants: {len(variants)} | Expected: {expected_variants}")
                print(f"    Generated: {variants}")
                
                self.test_results.append({
                    'artist': artist,
                    'album': album,
                    'variants_generated': len(variants),
                    'has_expected_variants': has_expected,
                    'success': len(variants) >= 2 and has_expected,
                    'complexity': case.get('complexity', 'Unknown')
                })
                
            except Exception as e:
                print(f"{i+1:2d}. {artist} - {album}")
                print(f"    ERROR: {e}")
                self.test_results.append({
                    'artist': artist,
                    'album': album,
                    'error': str(e),
                    'success': False
                })
        
        end_time = time.time()
        
        # Calculate results
        total_cases = len(all_cases)
        success_rate = successful_generations / total_cases
        processing_time = end_time - start_time
        
        print("\n" + "=" * 60)
        print("📊 PERFORMANCE RESULTS")
        print("=" * 60)
        print(f"Total test cases: {total_cases}")
        print(f"Successful generations: {successful_generations}")
        print(f"Success rate: {success_rate:.1%}")
        print(f"Unicode cases: {unicode_cases}")
        print(f"ASCII cases: {ascii_cases}")
        print(f"Processing time: {processing_time:.3f} seconds")
        print(f"Average time per query: {processing_time/total_cases*1000:.2f}ms")
        
        # Analyze by complexity
        complexity_stats = {}
        for result in self.test_results:
            complexity = result.get('complexity', 'Unknown')
            if complexity not in complexity_stats:
                complexity_stats[complexity] = {'total': 0, 'successful': 0}
            complexity_stats[complexity]['total'] += 1
            if result.get('success', False):
                complexity_stats[complexity]['successful'] += 1
        
        print(f"\n📈 SUCCESS RATE BY COMPLEXITY:")
        for complexity, stats in complexity_stats.items():
            rate = stats['successful'] / stats['total'] if stats['total'] > 0 else 0
            print(f"  {complexity}: {rate:.1%} ({stats['successful']}/{stats['total']})")
        
        # Performance targets
        print(f"\n🎯 PERFORMANCE TARGETS:")
        print(f"  ✅ Success rate ≥95%: {'PASS' if success_rate >= 0.95 else 'FAIL'} ({success_rate:.1%})")
        print(f"  ✅ Processing <2ms/query: {'PASS' if processing_time/total_cases < 0.002 else 'FAIL'} ({processing_time/total_cases*1000:.2f}ms)")
        print(f"  ✅ Unicode handling: {'PASS' if unicode_cases > 0 else 'FAIL'} ({unicode_cases} cases)")
        
        return {
            'total_cases': total_cases,
            'successful_generations': successful_generations,
            'success_rate': success_rate,
            'unicode_cases': unicode_cases,
            'ascii_cases': ascii_cases,
            'processing_time': processing_time,
            'avg_time_per_query': processing_time / total_cases,
            'complexity_stats': complexity_stats,
            'meets_performance_targets': success_rate >= 0.95 and processing_time/total_cases < 0.002
        }

def main():
    """Main test execution"""
    tester = UnicodeQueryTester()
    results = tester.run_performance_test()
    
    # Save results
    timestamp = time.strftime("%Y%m%d_%H%M%S")
    results_file = f"unicode_performance_results_{timestamp}.json"
    
    with open(results_file, 'w', encoding='utf-8') as f:
        json.dump({
            'timestamp': timestamp,
            'summary': results,
            'detailed_results': tester.test_results
        }, f, indent=2, ensure_ascii=False)
    
    print(f"\n💾 Results saved to: {results_file}")
    
    # Final assessment
    if results['meets_performance_targets']:
        print("\n🎉 SUCCESS: Unicode query builder meets all performance targets!")
        print("Ready for production deployment.")
    else:
        print("\n⚠️  REVIEW NEEDED: Some performance targets not met.")
        print("Consider optimization before deployment.")

if __name__ == "__main__":
    main()