using System.Collections.Generic;

namespace Qobuzarr.Tests.TestData
{
    /// <summary>
    /// Comprehensive test data set for search edge cases.
    /// These test cases cover various scenarios that are challenging for search algorithms.
    /// </summary>
    public static class SearchEdgeCases
    {
        /// <summary>
        /// Album titles with special characters that need proper escaping/handling
        /// </summary>
        public static readonly List<SearchTestCase> SpecialCharacterAlbums = new()
        {
            // Punctuation and symbols
            new("AC/DC", "Back in Black", "Slash in artist name"),
            new("Guns N' Roses", "Appetite for Destruction", "Apostrophe in artist name"),
            new("Blue Öyster Cult", "(Don't Fear) The Reaper", "Umlaut and parentheses"),
            new("Ke$ha", "Animal", "Dollar sign in artist name"),
            new("*NSYNC", "No Strings Attached", "Asterisk at start"),
            new("!!!","Louden Up Now", "Multiple exclamation marks as artist name"),
            new("?uestlove", "Mo' Meta Blues", "Question mark at start"),
            new("Sunn O)))", "Black One", "Multiple parentheses"),
            new("Earth, Wind & Fire", "September", "Comma and ampersand"),
            new("+44", "When Your Heart Stops Beating", "Plus sign at start"),
            new("blink-182", "Enema of the State", "Hyphen with numbers"),
            new(".38 Special", "Hold On Loosely", "Period at start with numbers"),
            
            // HTML/XML entities that might need escaping
            new("Simon & Garfunkel", "Bridge Over Troubled Water", "Ampersand entity"),
            new("Crosby, Stills, Nash & Young", "Déjà Vu", "Multiple commas and ampersand"),
            new("<|°_°|>", "Robot Face", "Angle brackets and special chars"),
            
            // Mathematical symbols
            new("∆", "∆", "Greek delta symbol"),
            new("≠", "≠", "Not equal symbol"),
            new("∞", "∞", "Infinity symbol"),
            
            // Quotes and apostrophes
            new("\"Weird Al\" Yankovic", "Mandatory Fun", "Quoted nickname"),
            new("She & Him", "Volume One", "Ampersand in simple name"),
            new("'Til Tuesday", "Voices Carry", "Apostrophe at start"),
            new("\"Heroes\"", "David Bowie", "Quoted album title"),
            
            // Special cases with year indicators
            new("(What's the Story) Morning Glory?", "Oasis", "Parentheses and question mark"),
            new("[1995]", "Album Title", "Year in square brackets"),
            new("{Various Artists}", "Compilation", "Curly braces"),
        };

        /// <summary>
        /// Non-English and Unicode test cases
        /// </summary>
        public static readonly List<SearchTestCase> InternationalAlbums = new()
        {
            // Japanese
            new("宇多田ヒカル", "First Love", "Japanese artist name (Utada Hikaru)"),
            new("BABYMETAL", "メタル・レジスタンス", "Japanese album title"),
            new("きゃりーぱみゅぱみゅ", "もしもし原宿", "Hiragana artist name"),
            
            // Korean
            new("방탄소년단", "MAP OF THE SOUL: 7", "Korean artist name (BTS)"),
            new("블랙핑크", "THE ALBUM", "Korean artist name (BLACKPINK)"),
            
            // Chinese
            new("周杰倫", "范特西", "Traditional Chinese (Jay Chou)"),
            new("邓紫棋", "启示录", "Simplified Chinese (G.E.M.)"),
            
            // Russian/Cyrillic
            new("Машина времени", "Поворот", "Cyrillic artist and album"),
            new("ДДТ", "Чёрный пёс Петербург", "Cyrillic with diacritics"),
            
            // Arabic
            new("فيروز", "معرفتي فيك", "Arabic artist (Fairuz)"),
            new("أم كلثوم", "الأطلال", "Arabic with special characters"),
            
            // Mixed scripts
            new("m-flo loves BONNIE PINK", "Love Song", "Mixed English/Japanese"),
            new("BoA보아", "Only One", "Mixed Korean/English"),
            
            // Special diacritics
            new("Björk", "Homogenic", "Icelandic characters"),
            new("Sigur Rós", "Ágætis byrjun", "Icelandic characters"),
            new("Mónica Naranjo", "Minage", "Spanish accents"),
            new("Françoise Hardy", "La Question", "French accents"),
            new("Émilie Simon", "Émilie Simon", "French accents in both"),
            new("Züri West", "Haubi Songs", "German umlaut"),
            new("Mägo de Oz", "Finisterra", "Spanish with umlaut"),
        };

        /// <summary>
        /// Albums with similar names that test disambiguation
        /// </summary>
        public static readonly List<SearchTestCase> SimilarNameAlbums = new()
        {
            // Same album title, different artists
            new("Pink Floyd", "The Wall", "Classic rock version"),
            new("Roger Waters", "The Wall", "Live version by original member"),
            
            // Very similar titles
            new("The Beatles", "Let It Be", "Original"),
            new("The Beatles", "Let It Be... Naked", "Remastered version"),
            new("The Replacements", "Let It Be", "Different artist, same title"),
            
            // Greatest hits variations
            new("Queen", "Greatest Hits", "Original compilation"),
            new("Queen", "Greatest Hits II", "Second compilation"),
            new("Queen", "Greatest Hits III", "Third compilation"),
            
            // Self-titled albums
            new("Metallica", "Metallica", "The Black Album"),
            new("The Beatles", "The Beatles", "The White Album"),
            new("Weezer", "Weezer", "The Blue Album"),
            new("Weezer", "Weezer", "The Green Album"),
            new("Weezer", "Weezer", "The Red Album"),
            
            // Live vs Studio
            new("Nirvana", "MTV Unplugged in New York", "Live album"),
            new("Nirvana", "Nevermind", "Studio album"),
            
            // Deluxe/Special editions
            new("Adele", "21", "Standard edition"),
            new("Adele", "21 (Deluxe Edition)", "Deluxe version"),
            new("Adele", "21 (Target Exclusive)", "Store exclusive"),
        };

        /// <summary>
        /// Edge cases for album/artist name sanitization
        /// </summary>
        public static readonly List<SearchTestCase> SanitizationCases = new()
        {
            // File system unfriendly characters
            new("AC/DC", "High Voltage", "Forward slash"),
            new("Question?", "Album:", "Question mark and colon"),
            new("*NSYNC", "Celebrity", "Asterisk"),
            new("P!nk", "Funhouse", "Exclamation in name"),
            new("Alo<e Blacc", "Title>", "Angle brackets"),
            new("Artist|Pipe", "Album|Name", "Pipe character"),
            
            // Path traversal attempts (security)
            new("../../../etc", "passwd", "Path traversal attempt"),
            new(".\\Windows\\System32", "config", "Windows path attempt"),
            new("~/.ssh", "id_rsa", "Unix home directory attempt"),
            
            // Excessive whitespace
            new("Artist    Name", "Album     Title", "Multiple spaces"),
            new("\tTab\tArtist", "\tTab\tAlbum", "Tab characters"),
            new(" Leading Space", " Leading Album", "Leading spaces"),
            new("Trailing Space ", "Trailing Album ", "Trailing spaces"),
            new("\nNewline\nArtist", "\nNewline\nAlbum", "Newline characters"),
            
            // Zero-width and invisible characters
            new("Arti​st", "Alb​um", "Zero-width space (U+200B)"),
            new("Arti‌st", "Alb‌um", "Zero-width non-joiner (U+200C)"),
            new("Arti‍st", "Alb‍um", "Zero-width joiner (U+200D)"),
            
            // Very long names
            new("This Is An Extremely Long Artist Name That Might Cause Issues With Database Field Lengths Or File System Path Limitations When Combined With Album Names",
                "This Is An Extremely Long Album Title That When Combined With The Artist Name And Track Titles Could Exceed Maximum Path Lengths On Various Operating Systems",
                "Extremely long names"),
            
            // Empty or null-like values
            new("", "Untitled Album", "Empty artist name"),
            new("Unknown Artist", "", "Empty album name"),
            new("null", "null", "Literal null strings"),
            new("None", "None", "Python-like None"),
            new("undefined", "undefined", "JavaScript-like undefined"),
            new("NaN", "NaN", "Not a Number"),
        };

        /// <summary>
        /// Edge cases for search query parsing
        /// </summary>
        public static readonly List<SearchTestCase> QueryParsingCases = new()
        {
            // Boolean-like operators in names
            new("AND", "OR", "SQL-like operators as names"),
            new("The The", "This Is the Day", "Repeated words"),
            new("YES", "90125", "All caps common word"),
            new("No Doubt", "Tragic Kingdom", "Negation in name"),
            new("Not", "Not", "Pure negation"),
            
            // Search operator-like content
            new("Artist +Plus", "Album -Minus", "Plus/minus in names"),
            new("\"Quoted\" Artist", "\"Quoted\" Album", "Pre-quoted content"),
            new("Artist*", "*Album", "Wildcard characters"),
            new("Artist?", "?Album", "Question wildcards"),
            new("(Artist)", "(Album)", "Already parenthesized"),
            
            // Version indicators
            new("Album (2019 Remaster)", "Artist", "Remaster indicator"),
            new("Album [Deluxe Edition]", "Artist", "Edition in brackets"),
            new("Album {Explicit}", "Artist", "Explicit marker"),
            new("Album <Single Version>", "Artist", "Version in angles"),
            
            // Numeric edge cases
            new("1", "1", "Single digit"),
            new("0", "0", "Zero"),
            new("-1", "-1", "Negative number"),
            new("3.14159", "2.71828", "Decimal numbers"),
            new("1984", "1984", "Year as album name"),
            new("99 Luftballons", "Nena", "Number at start"),
            new("Blink-182", "Take Off Your Pants and Jacket", "Number at end"),
            
            // Case sensitivity tests
            new("nirVAna", "nevERminD", "Mixed case"),
            new("SHOUTING", "LOUD ALBUM", "All caps"),
            new("whispering", "quiet album", "All lowercase"),
            new("CamelCaseArtist", "PascalCaseAlbum", "Programming case styles"),
        };

        /// <summary>
        /// Combined test case class
        /// </summary>
        public class SearchTestCase
        {
            public string ArtistName { get; }
            public string AlbumTitle { get; }
            public string Description { get; }
            public string ExpectedIssue { get; }

            public SearchTestCase(string artistName, string albumTitle, string description, string expectedIssue = null)
            {
                ArtistName = artistName;
                AlbumTitle = albumTitle;
                Description = description;
                ExpectedIssue = expectedIssue ?? "Should handle correctly";
            }

            public override string ToString() => $"{ArtistName} - {AlbumTitle} ({Description})";
        }

        /// <summary>
        /// Get all test cases
        /// </summary>
        public static IEnumerable<SearchTestCase> AllTestCases
        {
            get
            {
                foreach (var testCase in SpecialCharacterAlbums) yield return testCase;
                foreach (var testCase in InternationalAlbums) yield return testCase;
                foreach (var testCase in SimilarNameAlbums) yield return testCase;
                foreach (var testCase in SanitizationCases) yield return testCase;
                foreach (var testCase in QueryParsingCases) yield return testCase;
            }
        }

        /// <summary>
        /// Get test cases by category
        /// </summary>
        public static IEnumerable<SearchTestCase> GetByCategory(TestCategory category)
        {
            return category switch
            {
                TestCategory.SpecialCharacters => SpecialCharacterAlbums,
                TestCategory.International => InternationalAlbums,
                TestCategory.SimilarNames => SimilarNameAlbums,
                TestCategory.Sanitization => SanitizationCases,
                TestCategory.QueryParsing => QueryParsingCases,
                _ => AllTestCases
            };
        }

        public enum TestCategory
        {
            SpecialCharacters,
            International,
            SimilarNames,
            Sanitization,
            QueryParsing,
            All
        }
    }
}
