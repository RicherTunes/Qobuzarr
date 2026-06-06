using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a music album from the Qobuz API.
    /// Contains comprehensive metadata including track listings, artist information, and quality specifications.
    /// </summary>
    public class QobuzAlbum
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("artist")]
        public QobuzArtist Artist { get; set; }

        [JsonProperty("artists")]
        public List<QobuzArtist> Artists { get; set; } = new List<QobuzArtist>();

        [JsonProperty("released_at")]
        public long ReleasedAtTimestamp { get; set; }

        [JsonProperty("release_date_original")]
        public string ReleaseDateOriginal { get; set; }

        [JsonProperty("release_date_stream")]
        public string ReleaseDateStream { get; set; }

        [JsonProperty("release_date_download")]
        public string ReleaseDateDownload { get; set; }

        [JsonProperty("label")]
        public QobuzLabel Label { get; set; }

        [JsonProperty("genre")]
        public QobuzGenre Genre { get; set; }

        [JsonProperty("genres_list")]
        public List<string> GenresList { get; set; } = new List<string>();

        [JsonProperty("subgenre")]
        public QobuzGenre SubGenre { get; set; }

        [JsonProperty("image")]
        public QobuzImage Image { get; set; }

        [JsonProperty("tracks")]
        public QobuzTracksContainer TracksContainer { get; set; }

        [JsonProperty("tracks_count")]
        public int TracksCount { get; set; }

        [JsonProperty("media_count")]
        public int MediaCount { get; set; } = 1;

        [JsonProperty("duration")]
        public int DurationSeconds { get; set; }

        [JsonProperty("parental_warning")]
        public bool ParentalWarning { get; set; }

        [JsonProperty("popularity")]
        public int? Popularity { get; set; }

        [JsonProperty("awards")]
        public List<QobuzAward> Awards { get; set; } = new List<QobuzAward>();

        [JsonProperty("articles")]
        public List<QobuzArticle> Articles { get; set; } = new List<QobuzArticle>();

        [JsonProperty("goodies")]
        public List<QobuzGoodie> Goodies { get; set; } = new List<QobuzGoodie>();

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("description_language")]
        public string DescriptionLanguage { get; set; }

        [JsonProperty("purchasable")]
        public bool Purchasable { get; set; }

        [JsonProperty("streamable")]
        public bool Streamable { get; set; }

        [JsonProperty("downloadable")]
        public bool Downloadable { get; set; }

        [JsonProperty("sampleable")]
        public bool Sampleable { get; set; }

        [JsonProperty("displayable")]
        public bool Displayable { get; set; }

        [JsonProperty("previewable")]
        public bool Previewable { get; set; }

        [JsonProperty("maximum_bit_depth")]
        public int? MaximumBitDepth { get; set; }

        [JsonProperty("maximum_sampling_rate")]
        public double? MaximumSampleRate { get; set; }

        [JsonProperty("maximum_channel_count")]
        public int? MaximumChannelCount { get; set; }

        [JsonProperty("copyright")]
        public string Copyright { get; set; }

        [JsonProperty("upc")]
        public string UPC { get; set; }

        /// <summary>
        /// Get release date as DateTime
        /// </summary>
        public DateTime ReleaseDate
        {
            get
            {
                // Range-guard the epoch via Common's fail-closed TimeParsing so an out-of-range timestamp falls
                // through to the ISO strings below rather than throwing ArgumentOutOfRangeException out of this getter.
                if (ReleasedAtTimestamp > 0 && TimeParsing.TryFromUnixTimeSeconds(ReleasedAtTimestamp, out var releasedAt))
                {
                    return releasedAt.DateTime;
                }

                // Qobuz release-date strings are always Gregorian ISO (e.g. "2021-05-14"); parse them with the
                // invariant culture so a non-Gregorian current culture (e.g. Thai Buddhist) doesn't shift the year.
                if (DateTime.TryParse(ReleaseDateOriginal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var originalDate))
                {
                    return originalDate;
                }

                if (DateTime.TryParse(ReleaseDateStream, CultureInfo.InvariantCulture, DateTimeStyles.None, out var streamDate))
                {
                    return streamDate;
                }

                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Get album duration as TimeSpan
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

        /// <summary>
        /// Get all tracks from the album
        /// </summary>
        public List<QobuzTrack> GetTracks()
        {
            return TracksContainer?.Items ?? new List<QobuzTrack>();
        }

        /// <summary>
        /// Get full album title including version if available.
        /// Version field is sanitized to prevent injection attacks.
        /// </summary>
        public string GetFullTitle()
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "Unknown Album" : Title;

            // Sanitize version to prevent injection attacks
            var sanitizedVersion = MetadataSanitizer.SanitizeVersion(Version);

            if (!string.IsNullOrWhiteSpace(sanitizedVersion) && !ContainsStandaloneVersion(title, sanitizedVersion))
            {
                return $"{title} ({sanitizedVersion})";
            }
            return title;
        }

        private static bool ContainsStandaloneVersion(string title, string version)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            if (title.Contains($"({version})", StringComparison.OrdinalIgnoreCase) ||
                title.Contains($"[{version}]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var index = 0;
            while (true)
            {
                index = title.IndexOf(version, index, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                var beforeIndex = index - 1;
                var afterIndex = index + version.Length;

                var beforeOk = beforeIndex < 0 || !char.IsLetterOrDigit(title[beforeIndex]);
                var afterOk = afterIndex >= title.Length || !char.IsLetterOrDigit(title[afterIndex]);

                if (beforeOk && afterOk)
                {
                    return true;
                }

                index++;
            }
        }

        /// <summary>
        /// Get primary artist name
        /// </summary>
        public string GetArtistName()
        {
            return Artist?.Name ?? "Various Artists";
        }

        /// <summary>
        /// Get all contributing artists
        /// </summary>
        public List<string> GetAllArtistNames()
        {
            var artistNames = new List<string>();

            if (Artist?.Name.IsNotNullOrWhiteSpace() == true)
            {
                artistNames.Add(Artist.Name);
            }

            if (Artists != null)
            {
                artistNames.AddRange(Artists.Where(a => a?.Name.IsNotNullOrWhiteSpace() == true).Select(a => a.Name));
            }

            return artistNames.Distinct().ToList();
        }

        /// <summary>
        /// Get safe folder name for file system
        /// </summary>
        public string GetSafeFolderName()
        {
            var artist = GetArtistName();
            var title = GetFullTitle();
            var year = ReleaseDate.Year > 1900 ? ReleaseDate.Year.ToString() : "";

            var folderName = year.IsNotNullOrWhiteSpace() ? $"{artist} - {title} ({year})" : $"{artist} - {title}";

            // Replace illegal filesystem characters
            var illegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in illegalChars)
            {
                folderName = folderName.Replace(c, '_');
            }

            // Trim and limit length
            folderName = folderName.Trim();
            if (folderName.Length > 200)
            {
                folderName = folderName.Substring(0, 200).Trim();
            }

            return folderName;
        }

        /// <summary>
        /// Get primary genre
        /// </summary>
        public string? GetGenre()
        {
            if (Genre?.Name.IsNotNullOrWhiteSpace() == true)
                return Genre.Name;

            return GenresList?.FirstOrDefault();
        }

        /// <summary>
        /// Get record label name
        /// </summary>
        public string GetLabelName()
        {
            return Label?.Name ?? "Unknown Label";
        }

        /// <summary>
        /// Check if album has Hi-Res quality available
        /// </summary>
        public bool HasHiResQuality()
        {
            return MaximumSampleRate > 48000 || MaximumBitDepth > 16;
        }

        /// <summary>
        /// Get estimated total album size for a given format
        /// </summary>
        public long GetEstimatedTotalSize(int formatId)
        {
            return GetTracks().Sum(track => track.GetEstimatedFileSize(formatId));
        }

        /// <summary>
        /// Check if album has explicit content
        /// </summary>
        public bool IsExplicit()
        {
            return ParentalWarning || GetTracks().Any(t => t.IsExplicit());
        }
    }

    /// <summary>
    /// Container for paginated track listings within an album.
    /// Supports partial loading of tracks for large albums.
    /// </summary>
    public class QobuzTracksContainer
    {
        [JsonProperty("items")]
        public List<QobuzTrack> Items { get; set; } = new List<QobuzTrack>();

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; }
    }

    /// <summary>
    /// Represents a record label in the Qobuz catalog.
    /// </summary>
    public class QobuzLabel
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("albums_count")]
        public int? AlbumsCount { get; set; }

        [JsonProperty("supplier_id")]
        public string SupplierId { get; set; }
    }

    /// <summary>
    /// Represents a music genre classification in Qobuz.
    /// </summary>
    public class QobuzGenre
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }
    }

    /// <summary>
    /// Represents an award or accolade received by an album.
    /// </summary>
    public class QobuzAward
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("name_short")]
        public string NameShort { get; set; }

        [JsonProperty("award_type")]
        public string AwardType { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }
    }

    /// <summary>
    /// Represents an editorial article or review associated with an album.
    /// </summary>
    public class QobuzArticle
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// Represents downloadable extras associated with an album (e.g., liner notes, artwork).
    /// </summary>
    public class QobuzGoodie
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("file_format_id")]
        public int? FileFormatId { get; set; }
    }
}
