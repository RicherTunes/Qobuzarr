using System;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Implementation of title generation logic.
    /// Extracted from QobuzParser god class to follow Single Responsibility Principle.
    /// </summary>
    public class TitleGenerator : ITitleGenerator
    {
        private readonly Logger _logger;

        public TitleGenerator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string GenerateQualitySpecificTitle(QobuzAlbum album, QobuzAudioQuality quality, int year)
        {
            var artist = MetadataSanitizer.SanitizeArtistName(album.GetArtistName());
            var albumTitle = album.Title; // Use base title without version
            var version = album.Version?.Trim(); // Get version separately
            
            // Generate quality string
            var formatStr = quality switch
            {
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                QobuzAudioQuality.FLACLossless => "FLAC",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC 24bit 96kHz",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC 24bit 192kHz",
                _ => "Unknown"
            };
            
            // ARCHITECTURAL DECISION: Dual-format title generation based on album type
            // Edition albums use hyphen format to trigger Lidarr's version extraction (Parser.cs:73)
            // Standard albums use existing bracket format for backward compatibility
            
            // Check for edition info in Version field OR album title
            var hasVersionField = !string.IsNullOrWhiteSpace(version) && ContainsEditionKeywords(version);
            var hasEditionInTitle = ContainsEditionKeywords(albumTitle);
            
            _logger.Trace("🔍 EDITION CHECK: Album='{0}', HasEdition={1}", albumTitle, hasVersionField || hasEditionInTitle);
            
            if (hasVersionField || hasEditionInTitle)
            {
                // Extract version from title if not in Version field
                var versionToUse = version;
                var cleanAlbumTitle = albumTitle;
                
                if (string.IsNullOrWhiteSpace(versionToUse) && hasEditionInTitle)
                {
                    // Extract edition info from title: "Album (Deluxe Edition)" → version="Deluxe Edition"
                    versionToUse = ExtractVersionFromTitle(albumTitle);
                    cleanAlbumTitle = albumTitle.Replace($"({versionToUse})", "").Replace($"[{versionToUse}]", "").Trim();
                    _logger.Debug("Extracted version from title: '{0}' → album='{1}', version='{2}'", 
                        albumTitle, cleanAlbumTitle, versionToUse);
                }
                
                // Test multiple elegant formats to find the best one
                var formats = new[]
                {
                    $"{artist} - {cleanAlbumTitle} [{versionToUse}] - WEB - {year}",           // Bracket format
                    $"{artist} - {cleanAlbumTitle} ({versionToUse}) - WEB - {year}",          // Parentheses format  
                    $"{artist} - {cleanAlbumTitle} • {versionToUse} • WEB • {year}",          // Bullet format
                    $"{artist}-{cleanAlbumTitle}-[{versionToUse}]-WEB-{year}",                // Mixed hyphen-bracket
                    $"{artist}-{cleanAlbumTitle}-{versionToUse}-WEB-{year}"                   // Original hyphen
                };
                
                // For now, use the first (bracket) format - most elegant and likely to work
                var chosenFormat = formats[0];
                _logger.Debug("🎯 EDITION ALBUM: Using elegant format for '{0}'", albumTitle);
                return chosenFormat;
            }
            
            // Standard format for non-edition albums
            var yearStr = year > 0 ? $" ({year})" : "";
            var explicitStr = album.ParentalWarning ? " [Explicit]" : "";
            var liveIndicator = IsLiveAlbum(albumTitle) ? " [LIVE]" : "";
            
            var standardTitle = $"{artist} - {albumTitle}{yearStr}{explicitStr}{liveIndicator} [{formatStr}] [WEB]";
            _logger.Trace("Standard album format: '{0}'", standardTitle);
            return standardTitle;
        }

        public string GenerateHyphenFormatTitle(string artist, string albumTitle, string version, string formatStr, int year)
        {
            // Sanitize components to prevent parser issues
            artist = SanitizeForHyphenFormat(artist);
            albumTitle = SanitizeForHyphenFormat(albumTitle);
            version = SanitizeForHyphenFormat(version);
            
            // Handle missing year gracefully
            var yearStr = year > 1900 ? year.ToString() : DateTime.Now.Year.ToString();
            
            // Primary format: Artist-Album-Version-Source-Year
            // This matches the regex at Parser.cs:73 for version extraction
            var primaryFormat = $"{artist}-{albumTitle}-{version}-WEB-{yearStr}";
            
            // Validate format won't break parser (basic sanity checks)
            if (primaryFormat.Length > 500) // Excessive length might cause issues
            {
                _logger.Warn("Hyphen format title too long ({0} chars), truncating components", primaryFormat.Length);
                
                // Intelligently truncate to preserve important info
                if (version.Length > 50)
                {
                    version = version.Substring(0, 47) + "...";
                }
                if (albumTitle.Length > 100)
                {
                    albumTitle = albumTitle.Substring(0, 97) + "...";
                }
                
                primaryFormat = $"{artist}-{albumTitle}-{version}-WEB-{yearStr}";
            }
            
            // Final validation pass
            primaryFormat = ValidateHyphenFormat(primaryFormat);
            
            // Log format for debugging
            _logger.Trace("Hyphen format components: Artist='{0}', Album='{1}', Version='{2}', Year='{3}'",
                artist, albumTitle, version, yearStr);
            
            return primaryFormat;
        }

        public string SanitizeForHyphenFormat(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Unknown";
                
            // Replace problematic characters that might confuse parser
            text = text.Replace("/", " ");
            text = text.Replace("\\", " ");
            text = text.Replace(":", " ");
            text = text.Replace("|", " ");
            text = text.Replace("?", "");
            text = text.Replace("*", "");
            text = text.Replace("<", "");
            text = text.Replace(">", "");
            text = text.Replace("\"", "'");
            
            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            // Ensure no leading/trailing hyphens
            text = text.Trim('-');
            
            return text;
        }

        public string ValidateHyphenFormat(string format)
        {
            // Ensure no double hyphens that might confuse parser
            format = Regex.Replace(format, @"-{2,}", "-");
            
            // Ensure format has expected structure (at least 4 hyphens for Artist-Album-Version-Source-Year)
            var hyphenCount = format.Count(c => c == '-');
            if (hyphenCount < 4)
            {
                _logger.Warn("Hyphen format has insufficient delimiters ({0}), format may not parse correctly: '{1}'",
                    hyphenCount, format);
            }
            
            return format;
        }

        public string ExtractVersionFromTitle(string title)
        {
            // Extract content from parentheses: "Album (Deluxe Edition)" → "Deluxe Edition"
            var parenthesesMatch = Regex.Match(title, @"\(([^)]+)\)");
            if (parenthesesMatch.Success)
            {
                var content = parenthesesMatch.Groups[1].Value;
                if (ContainsEditionKeywords(content))
                {
                    return content;
                }
            }
            
            // Extract content from brackets: "Album [Live at Venue]" → "Live at Venue"  
            var bracketsMatch = Regex.Match(title, @"\[([^\]]+)\]");
            if (bracketsMatch.Success)
            {
                var content = bracketsMatch.Groups[1].Value;
                if (ContainsEditionKeywords(content))
                {
                    return content;
                }
            }
            
            return "";
        }

        public bool ContainsEditionKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
                
            // Comprehensive edition patterns based on production data analysis
            var editionKeywords = new[] 
            { 
                // Core edition types
                "deluxe", "edition", "remaster", "anniversary", "expanded", 
                "special", "collector", "limited", "bonus",
                
                // Live album indicators
                "live at", "live in", "live", "concert", "unplugged", "acoustic",
                
                // Format variants  
                "remix", "instrumental", "extended", "radio",
                
                // Special releases
                "legacy", "archive", "complete", "sessions", "demos"
            };
            
            var hasEdition = editionKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            if (hasEdition)
            {
                _logger.Trace("Edition keywords detected in '{0}'", text);
            }
            
            return hasEdition;
        }

        public bool IsLiveAlbum(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;
                
            var liveTerms = new[] { " live", "(live)", "[live]", "live at", "live in", "concert", "unplugged" };
            return liveTerms.Any(term => albumTitle.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }
}