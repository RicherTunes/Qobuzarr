#!/usr/bin/env python3
"""
Qobuz Gap Validator

Validates predicted Unicode system gaps by testing manual search strategies against
the actual Qobuz API to confirm which albums are truly missed vs found.

Usage:
    python scripts/validate_qobuz_gaps.py
    python scripts/validate_qobuz_gaps.py --gaps-file scripts/unicode_gaps_analysis.json
    python scripts/validate_qobuz_gaps.py --qobuz-credentials qobuz_config.json
"""

import asyncio
import aiohttp
import json
import hashlib
import argparse
import time
import re
from dataclasses import dataclass, asdict
from typing import List, Dict, Optional, Tuple
import logging
from pathlib import Path
import os

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

@dataclass
class QobuzSearchResult:
    """Result of Qobuz search validation"""
    query: str
    found: bool
    result_count: int
    first_result: Optional[Dict]
    response_time_ms: float
    search_strategy: str

@dataclass 
class GapValidationResult:
    """Result of validating a predicted gap"""
    artist: str
    album: str
    lidarr_id: int
    predicted_gap: bool
    actually_missing: bool
    gap_confirmed: bool
    unicode_variants_tested: List[str]
    manual_strategies_tested: List[str]
    working_queries: List[str]
    best_working_query: Optional[str]
    search_results: List[QobuzSearchResult]
    validation_notes: List[str]

class QobuzSearchValidator:
    """Validates search gaps against actual Qobuz API (matches plugin authentication)"""
    
    def __init__(self, config: dict):
        self.config = config
        self.base_url = "https://www.qobuz.com/api.json/0.2"
        
        # Determine authentication method (same logic as plugin)
        from load_env import get_qobuz_auth_method
        self.auth_method, self.auth_params = get_qobuz_auth_method(config)
        
    async def validate_search_strategies(self, artist: str, album: str, 
                                       unicode_variants: List[str]) -> GapValidationResult:
        """Test multiple search strategies to see if album is truly missing"""
        
        search_results = []
        working_queries = []
        validation_notes = []
        manual_strategies = []  # Initialize here to fix scope issue
        
        # 1. Test the Unicode system variants first
        logger.info(f"INFO Testing Unicode variants for: {artist} - {album}")
        
        for variant in unicode_variants:
            result = await self.search_qobuz(variant, "unicode_variant")
            search_results.append(result)
            
            if result.found:
                working_queries.append(variant)
                logger.info(f"SUCCESS Unicode variant works: '{variant}' → {result.result_count} results")
        
        # 2. If Unicode variants fail, try manual strategies
        if not working_queries:
            logger.info(f"WARNING All Unicode variants failed, trying manual strategies...")
            
            manual_strategies = self.generate_manual_search_strategies(artist, album)
            
            for strategy_name, query in manual_strategies:
                result = await self.search_qobuz(query, strategy_name)
                search_results.append(result)
                
                if result.found:
                    working_queries.append(query)
                    validation_notes.append(f"Manual strategy '{strategy_name}' works: '{query}'")
                    logger.info(f"SUCCESS Manual strategy works: {strategy_name} → '{query}'")
        
        # 3. Advanced strategies if still failing
        if not working_queries:
            logger.info(f"WARNING Manual strategies failed, trying advanced approaches...")
            
            advanced_strategies = self.generate_advanced_search_strategies(artist, album)
            
            for strategy_name, query in advanced_strategies:
                result = await self.search_qobuz(query, strategy_name)
                search_results.append(result)
                
                if result.found:
                    working_queries.append(query)
                    validation_notes.append(f"Advanced strategy '{strategy_name}' works: '{query}'")
                    logger.info(f"SUCCESS Advanced strategy works: {strategy_name} → '{query}'")
        
        # Determine if gap is real
        predicted_gap = True  # We're testing predicted gaps
        actually_missing = len(working_queries) == 0
        gap_confirmed = predicted_gap and actually_missing
        
        best_working_query = working_queries[0] if working_queries else None
        
        if gap_confirmed:
            validation_notes.append("CONFIRMED GAP CONFIRMED: Album not found with any strategy")
        elif working_queries:
            validation_notes.append(f"🟡 FALSE POSITIVE: Album found with manual strategy")
        
        return GapValidationResult(
            artist=artist,
            album=album,
            lidarr_id=0,  # Will be filled from input data
            predicted_gap=predicted_gap,
            actually_missing=actually_missing,
            gap_confirmed=gap_confirmed,
            unicode_variants_tested=unicode_variants,
            manual_strategies_tested=[strategy for strategy, _ in manual_strategies],
            working_queries=working_queries,
            best_working_query=best_working_query,
            search_results=search_results,
            validation_notes=validation_notes
        )
    
    async def search_qobuz(self, query: str, strategy: str) -> QobuzSearchResult:
        """Perform actual Qobuz API search"""
        
        start_time = time.time()
        
        # Prepare search request
        url = f"{self.base_url}/album/search"
        params = {
            'query': query,
            'limit': 20,
            'country_code': 'CA'  # Same as plugin default
        }
        
        # Add authentication exactly like plugin (URL parameters)
        if self.auth_method == 'email' or self.auth_method == 'token':
            # Plugin uses: app_id={session.AppId}&user_auth_token={session.AuthToken}
            params['app_id'] = self.auth_params.get('app_id')
            if 'user_auth_token' in self.auth_params:
                params['user_auth_token'] = self.auth_params['user_auth_token']
        elif self.auth_method == 'app_only':
            params['app_id'] = self.auth_params.get('app_id')
        else:
            logger.warning("WARNING No Qobuz authentication configured - using anonymous access")
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(url, params=params) as response:
                    response_time = (time.time() - start_time) * 1000
                    
                    if response.status != 200:
                        logger.warning(f"WARNING Qobuz API error {response.status} for query: '{query}'")
                        return QobuzSearchResult(
                            query=query,
                            found=False,
                            result_count=0,
                            first_result=None,
                            response_time_ms=response_time,
                            search_strategy=strategy
                        )
                    
                    data = await response.json()
                    albums = data.get('albums', {}).get('items', [])
                    
                    return QobuzSearchResult(
                        query=query,
                        found=len(albums) > 0,
                        result_count=len(albums),
                        first_result=albums[0] if albums else None,
                        response_time_ms=response_time,
                        search_strategy=strategy
                    )
                    
        except Exception as e:
            logger.error(f"ERROR Search failed for '{query}': {e}")
            return QobuzSearchResult(
                query=query,
                found=False,
                result_count=0,
                first_result=None,
                response_time_ms=time.time() - start_time * 1000,
                search_strategy=strategy
            )
        finally:
            # Rate limiting - respect Qobuz API limits
            await asyncio.sleep(1.1)  # Just over 1 second per request
    
    def generate_manual_search_strategies(self, artist: str, album: str) -> List[Tuple[str, str]]:
        """Generate manual search strategies beyond Unicode system"""
        
        strategies = []
        
        # 1. Artist only (often works when full query fails)
        strategies.append(("artist_only", artist))
        
        # 2. Album only
        strategies.append(("album_only", album))
        
        # 3. Remove all parentheticals
        clean_album = re.sub(r'\s*\([^)]*\)\s*', ' ', album)
        clean_album = re.sub(r'\s*\[[^\]]*\]\s*', ' ', clean_album)
        clean_album = re.sub(r'\s+', ' ', clean_album).strip()
        if clean_album != album:
            strategies.append(("no_parentheticals", f"{artist} {clean_album}"))
        
        # 4. Remove special edition text
        no_edition = re.sub(r'\b(deluxe|special|anniversary|remaster|edition|expanded|collector|limited)\b', 
                           '', album, flags=re.IGNORECASE)
        no_edition = re.sub(r'\s+', ' ', no_edition).strip()
        if no_edition != album and no_edition:
            strategies.append(("no_edition_text", f"{artist} {no_edition}"))
        
        # 5. First word of album only (for very complex titles)
        album_words = album.split()
        if len(album_words) > 3:
            short_album = ' '.join(album_words[:2])
            strategies.append(("short_album", f"{artist} {short_album}"))
        
        # 6. Remove featured artists
        no_featured = re.sub(r'\s+(feat|ft|featuring)\.?\s+.*$', '', f"{artist} {album}", flags=re.IGNORECASE)
        if no_featured != f"{artist} {album}":
            strategies.append(("no_featured", no_featured.strip()))
        
        # 7. Common misspellings/variations
        if 'the ' in artist.lower():
            no_the = artist.replace('The ', '').replace('the ', '')
            strategies.append(("no_the", f"{no_the} {album}"))
        
        return strategies
    
    def generate_advanced_search_strategies(self, artist: str, album: str) -> List[Tuple[str, str]]:
        """Generate advanced search strategies for really difficult cases"""
        
        strategies = []
        
        # 1. Phonetic approximations (very basic)
        phonetic_artist = self.simple_phonetic(artist)
        if phonetic_artist != artist:
            strategies.append(("phonetic_artist", f"{phonetic_artist} {album}"))
        
        # 2. Remove all numbers
        no_numbers = re.sub(r'\b\d+\b', '', f"{artist} {album}")
        no_numbers = re.sub(r'\s+', ' ', no_numbers).strip()
        if no_numbers != f"{artist} {album}":
            strategies.append(("no_numbers", no_numbers))
        
        # 3. Only alphanumeric + spaces
        alpha_only = re.sub(r'[^\w\s]', ' ', f"{artist} {album}")
        alpha_only = re.sub(r'\s+', ' ', alpha_only).strip()
        if alpha_only != f"{artist} {album}":
            strategies.append(("alphanumeric_only", alpha_only))
        
        # 4. Keywords only (most important words)
        keywords = self.extract_keywords(artist, album)
        if keywords:
            strategies.append(("keywords_only", ' '.join(keywords)))
        
        return strategies
    
    def simple_phonetic(self, text: str) -> str:
        """Very basic phonetic approximation"""
        # Simple character replacements that might help
        replacements = {
            'ph': 'f', 'gh': 'g', 'ck': 'k', 'qu': 'kw',
            'x': 'ks', 'z': 's'
        }
        
        result = text.lower()
        for old, new in replacements.items():
            result = result.replace(old, new)
        
        return result
    
    def extract_keywords(self, artist: str, album: str) -> List[str]:
        """Extract most important keywords from artist and album"""
        
        # Common stop words to remove
        stop_words = {'the', 'a', 'an', 'and', 'or', 'but', 'in', 'on', 'at', 'to', 'for', 
                     'of', 'with', 'by', 'from', 'as', 'is', 'was', 'are', 'were', 'be', 'been'}
        
        # Combine and split
        all_words = f"{artist} {album}".lower().split()
        
        # Filter keywords
        keywords = []
        for word in all_words:
            # Remove punctuation
            clean_word = re.sub(r'[^\w]', '', word)
            
            # Skip short words, numbers, stop words
            if (len(clean_word) >= 3 and 
                not clean_word.isdigit() and 
                clean_word not in stop_words):
                keywords.append(clean_word)
        
        # Return most distinctive keywords (limit to 4)
        return keywords[:4]

class GapValidator:
    """Main validator that processes gap predictions and validates them"""
    
    def __init__(self, config: dict):
        self.validator = QobuzSearchValidator(config)
        
    async def validate_gaps_from_file(self, gaps_file: str, 
                                    max_validations: Optional[int] = None) -> List[GapValidationResult]:
        """Load gaps from analysis file and validate them"""
        
        logger.info(f"LOADING Loading gaps from: {gaps_file}")
        
        with open(gaps_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        gaps = data.get('gaps', [])
        
        if not gaps:
            logger.warning("ERROR No gaps found in file")
            return []
        
        if max_validations:
            gaps = gaps[:max_validations]
            logger.info(f"NEXT Limiting validation to {max_validations} gaps")
        
        logger.info(f"INFO Validating {len(gaps)} predicted gaps against Qobuz API")
        print("WARNING Note: This will take time due to 1-second rate limiting per Qobuz API requirements")
        
        validation_results = []
        
        for i, gap_data in enumerate(gaps, 1):
            try:
                logger.info(f"VALIDATING Validating {i}/{len(gaps)}: {gap_data['artist']} - {gap_data['album']}")
                
                result = await self.validator.validate_search_strategies(
                    gap_data['artist'],
                    gap_data['album'], 
                    gap_data['generated_variants']
                )
                
                result.lidarr_id = gap_data['lidarr_id']
                validation_results.append(result)
                
                # Progress update
                if i % 10 == 0:
                    confirmed_gaps = sum(1 for r in validation_results if r.gap_confirmed)
                    false_positives = sum(1 for r in validation_results if not r.gap_confirmed)
                    logger.info(f"INFO Progress: {i}/{len(gaps)} | Confirmed gaps: {confirmed_gaps} | False positives: {false_positives}")
                
            except Exception as e:
                logger.error(f"ERROR Error validating {gap_data['artist']} - {gap_data['album']}: {e}")
                continue
        
        logger.info(f"COMPLETE Validation complete: {len(validation_results)} results")
        return validation_results
    
    def analyze_validation_results(self, results: List[GapValidationResult]) -> Dict:
        """Analyze validation results to identify real gaps and false positives"""
        
        if not results:
            return {'error': 'No validation results to analyze'}
        
        # Categorize results
        confirmed_gaps = [r for r in results if r.gap_confirmed]
        false_positives = [r for r in results if not r.gap_confirmed]
        
        # Analyze false positive patterns (where manual search worked)
        false_positive_patterns = {}
        for fp in false_positives:
            if fp.best_working_query:
                # What manual strategy worked?
                working_strategies = [sr.search_strategy for sr in fp.search_results if sr.found]
                for strategy in working_strategies:
                    false_positive_patterns[strategy] = false_positive_patterns.get(strategy, 0) + 1
        
        # Analyze confirmed gap characteristics
        gap_characteristics = {}
        for gap in confirmed_gaps:
            for note in gap.validation_notes:
                if 'CONFIRMED' in note:
                    gap_characteristics['truly_missing'] = gap_characteristics.get('truly_missing', 0) + 1
        
        # Character pattern analysis for gaps
        unsupported_chars_in_gaps = set()
        for gap in confirmed_gaps:
            for char in f"{gap.artist} {gap.album}":
                if ord(char) > 127:
                    unsupported_chars_in_gaps.add(char)
        
        return {
            'validation_summary': {
                'total_validated': len(results),
                'confirmed_gaps': len(confirmed_gaps),
                'false_positives': len(false_positives),
                'gap_confirmation_rate': len(confirmed_gaps) / len(results) if results else 0
            },
            'false_positive_analysis': {
                'successful_manual_strategies': dict(sorted(false_positive_patterns.items(), 
                                                          key=lambda x: x[1], reverse=True)),
                'examples': [
                    {
                        'artist': fp.artist,
                        'album': fp.album,
                        'unicode_variants_failed': fp.unicode_variants_tested,
                        'working_manual_query': fp.best_working_query,
                        'strategy_that_worked': fp.search_results[0].search_strategy if fp.search_results else None
                    }
                    for fp in false_positives[:5]
                ]
            },
            'confirmed_gaps_analysis': {
                'characteristics': gap_characteristics,
                'unsupported_characters': list(unsupported_chars_in_gaps),
                'examples': [
                    {
                        'artist': gap.artist,
                        'album': gap.album,
                        'variants_tried': gap.unicode_variants_tested + gap.manual_strategies_tested,
                        'why_failed': gap.validation_notes
                    }
                    for gap in confirmed_gaps[:5]
                ]
            },
            'unicode_system_improvements': self.generate_system_improvements(false_positives, confirmed_gaps)
        }
    
    def generate_system_improvements(self, false_positives: List[GapValidationResult], 
                                   confirmed_gaps: List[GapValidationResult]) -> List[str]:
        """Generate specific improvements for Unicode system based on validation"""
        
        improvements = []
        
        # Analyze what manual strategies work for false positives
        working_strategies = {}
        for fp in false_positives:
            for result in fp.search_results:
                if result.found:
                    working_strategies[result.search_strategy] = working_strategies.get(result.search_strategy, 0) + 1
        
        # Generate recommendations
        if working_strategies.get('artist_only', 0) > 3:
            improvements.append("Add artist-only fallback as higher priority variant")
        
        if working_strategies.get('no_parentheticals', 0) > 3:
            improvements.append("Improve parenthetical removal - make it more aggressive")
        
        if working_strategies.get('no_edition_text', 0) > 3:
            improvements.append("Enhance special edition text detection and removal")
        
        if working_strategies.get('short_album', 0) > 3:
            improvements.append("Add truncated album title variants for very long titles")
        
        # Analyze confirmed gaps for system limitations
        if len(confirmed_gaps) > 0:
            improvements.append(f"Investigate {len(confirmed_gaps)} truly missing albums - may be Qobuz catalog gaps")
        
        return improvements

async def main():
    """Main validation workflow"""
    # Load configuration from .env file
    from load_env import get_config
    config = get_config()
    
    parser = argparse.ArgumentParser(description="Validate Unicode system gaps against Qobuz API")
    parser.add_argument('--gaps-file', default='scripts/unicode_gaps_analysis.json',
                       help='Gap analysis file to validate')
    parser.add_argument('--qobuz-app-id', default=config['qobuz_app_id'],
                       help='Qobuz app ID')
    parser.add_argument('--qobuz-app-secret', default=config['qobuz_app_secret'],
                       help='Qobuz app secret')
    parser.add_argument('--user-token', default=config['qobuz_user_auth_token'],
                       help='Qobuz user token (optional)')
    parser.add_argument('--max-validations', type=int, default=50,
                       help='Maximum number of gaps to validate (due to rate limiting)')
    parser.add_argument('--output', default='scripts/gap_validation_results.json',
                       help='Output file for validation results')
    
    args = parser.parse_args()
    
    if not args.qobuz_app_id or not args.qobuz_app_secret:
        print("ERROR Error: Qobuz credentials required")
        print("Set environment variables: QOBUZ_APP_ID, QOBUZ_APP_SECRET")
        print("Or use --qobuz-app-id and --qobuz-app-secret arguments")
        return
    
    print("INFO Qobuz Gap Validation")
    print("=" * 50)
    print(f"INFO Gaps file: {args.gaps_file}")
    print(f"AUTH Qobuz App ID: {args.qobuz_app_id[:8]}...")
    print(f"MAX Max validations: {args.max_validations}")
    print(f"TIME Estimated time: {args.max_validations * 1.2 / 60:.1f} minutes")
    print("=" * 50)
    
    validator = GapValidator(config)
    
    try:
        # Validate gaps
        results = await validator.validate_gaps_from_file(
            args.gaps_file,
            max_validations=args.max_validations
        )
        
        if not results:
            print("ERROR No gaps validated")
            return
        
        # Analyze results  
        analysis = validator.analyze_validation_results(results)
        
        # Save results
        with open(args.output, 'w', encoding='utf-8') as f:
            json.dump({
                'validation_results': [asdict(result) for result in results],
                'analysis': analysis
            }, f, indent=2, ensure_ascii=False)
        
        # Display summary
        summary = analysis['validation_summary']
        print(f"\nINFO VALIDATION RESULTS")
        print("=" * 50)
        print(f"Total validated: {summary['total_validated']}")
        print(f"Confirmed gaps: {summary['confirmed_gaps']}")
        print(f"False positives: {summary['false_positives']}")
        print(f"Gap confirmation rate: {summary['gap_confirmation_rate']:.1%}")
        
        # Show successful manual strategies
        manual_strategies = analysis['false_positive_analysis']['successful_manual_strategies']
        if manual_strategies:
            print(f"\nIMPROVEMENTS MANUAL STRATEGIES THAT WORK:")
            for strategy, count in list(manual_strategies.items())[:5]:
                print(f"   • {strategy}: worked for {count} albums")
        
        # Show system improvements
        improvements = analysis['unicode_system_improvements']
        if improvements:
            print(f"\nNEXT RECOMMENDED UNICODE SYSTEM IMPROVEMENTS:")
            for improvement in improvements:
                print(f"   • {improvement}")
        
        # Show examples
        fp_examples = analysis['false_positive_analysis']['examples']
        if fp_examples:
            print(f"\nEXAMPLES FALSE POSITIVE EXAMPLES (Unicode system can be improved):")
            for example in fp_examples[:3]:
                print(f"   • {example['artist']} - {example['album']}")
                print(f"     Working query: '{example['working_manual_query']}'")
                print(f"     Strategy: {example['strategy_that_worked']}")
        
        gap_examples = analysis['confirmed_gaps_analysis']['examples']
        if gap_examples:
            print(f"\nCONFIRMED CONFIRMED GAPS (truly missing from Qobuz):")
            for example in gap_examples[:3]:
                print(f"   • {example['artist']} - {example['album']}")
                print(f"     Tried: {len(example['variants_tried'])} different queries")
        
        print(f"\nSAVED Detailed results saved to: {args.output}")
        
        if improvements:
            print(f"\nNEXT Next steps:")
            print("1. Implement recommended Unicode system improvements")
            print("2. Run: python scripts/generate_complex_test_cases.py")
            print("3. Add new test cases to ensure improvements work")
        
    except FileNotFoundError as e:
        print(f"ERROR Error: {e}")
        print("First run: python scripts/analyze_unicode_gaps.py")
    except Exception as e:
        logger.error(f"💥 Validation failed: {e}")
        raise

if __name__ == "__main__":
    asyncio.run(main())