using System;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Builders
{
    /// <summary>
    /// Builder pattern for creating test QobuzAlbum objects with fluent API
    /// </summary>
    public class QobuzAlbumBuilder
    {
        private string _id = "album123";
        private string _title = "Test Album";
        private QobuzArtist _artist = new QobuzArtist { Name = "Test Artist", Id = "artist123" };
        private QobuzLabel _label = new QobuzLabel { Name = "Test Label" };
        private QobuzGenre _genre = new QobuzGenre { Name = "Rock" };
        private DateTime _releaseDate = DateTime.Now.Date;
        private int _tracksCount = 10;
        private int _durationSeconds = 3000;
        private bool _streamable = true;
        private bool _purchasable = true;
        private bool _sampleable = false;
        private bool _parentalWarning = false;
        private string _upc = "123456789012";
        private QobuzImage _image = new QobuzImage 
        { 
            Small = "https://static.qobuz.com/images/covers/small/album123.jpg",
            Large = "https://static.qobuz.com/images/covers/large/album123.jpg"
        };
        private int _maximumBitDepth = 24;
        private int _maximumSampleRate = 192000;
        private QobuzTracksContainer _tracksContainer = null;

        /// <summary>
        /// Creates a new QobuzAlbumBuilder with default test values
        /// </summary>
        public static QobuzAlbumBuilder New() => new QobuzAlbumBuilder();

        /// <summary>
        /// Sets the album ID
        /// </summary>
        public QobuzAlbumBuilder WithId(string id)
        {
            _id = id;
            return this;
        }

        /// <summary>
        /// Sets the album title
        /// </summary>
        public QobuzAlbumBuilder WithTitle(string title)
        {
            _title = title;
            return this;
        }

        /// <summary>
        /// Sets the album artist
        /// </summary>
        public QobuzAlbumBuilder WithArtist(string name, string id = null)
        {
            _artist = new QobuzArtist 
            { 
                Name = name, 
                Id = id ?? $"artist_{name.ToLower().Replace(" ", "_")}"
            };
            return this;
        }

        /// <summary>
        /// Sets the record label
        /// </summary>
        public QobuzAlbumBuilder WithLabel(string labelName)
        {
            _label = new QobuzLabel { Name = labelName };
            return this;
        }

        /// <summary>
        /// Sets the genre
        /// </summary>
        public QobuzAlbumBuilder WithGenre(string genreName)
        {
            _genre = new QobuzGenre { Name = genreName };
            return this;
        }

        /// <summary>
        /// Sets the release date
        /// </summary>
        public QobuzAlbumBuilder WithReleaseDate(DateTime releaseDate)
        {
            _releaseDate = releaseDate;
            return this;
        }

        /// <summary>
        /// Sets the release date from year
        /// </summary>
        public QobuzAlbumBuilder WithReleaseYear(int year)
        {
            _releaseDate = new DateTime(year, 1, 1);
            return this;
        }

        /// <summary>
        /// Sets the track count and total duration
        /// </summary>
        public QobuzAlbumBuilder WithTracks(int count, int averageDurationSeconds = 300)
        {
            _tracksCount = count;
            _durationSeconds = count * averageDurationSeconds;
            return this;
        }

        /// <summary>
        /// Sets audio quality specifications
        /// </summary>
        public QobuzAlbumBuilder WithQuality(int bitDepth, int sampleRate)
        {
            _maximumBitDepth = bitDepth;
            _maximumSampleRate = sampleRate;
            return this;
        }

        /// <summary>
        /// Marks the album as not streamable
        /// </summary>
        public QobuzAlbumBuilder AsNotStreamable()
        {
            _streamable = false;
            return this;
        }

        /// <summary>
        /// Marks the album as having explicit content
        /// </summary>
        public QobuzAlbumBuilder AsExplicit()
        {
            _parentalWarning = true;
            return this;
        }

        /// <summary>
        /// Sets up as a single (1-3 tracks, short duration)
        /// </summary>
        public QobuzAlbumBuilder AsSingle()
        {
            _tracksCount = 2;
            _durationSeconds = 480; // 8 minutes total
            return this;
        }

        /// <summary>
        /// Sets up as an EP (4-6 tracks, medium duration)
        /// </summary>
        public QobuzAlbumBuilder AsEP()
        {
            _tracksCount = 5;
            _durationSeconds = 1200; // 20 minutes total
            return this;
        }

        /// <summary>
        /// Sets up as a full album (7+ tracks, long duration)
        /// </summary>
        public QobuzAlbumBuilder AsFullAlbum()
        {
            _tracksCount = 12;
            _durationSeconds = 3600; // 60 minutes total
            return this;
        }

        /// <summary>
        /// Sets up as a compilation album
        /// </summary>
        public QobuzAlbumBuilder AsCompilation()
        {
            _title = "Various Artists - " + _title;
            _artist = new QobuzArtist { Name = "Various Artists", Id = "various_artists" };
            _tracksCount = 15;
            _durationSeconds = 4500; // 75 minutes
            return this;
        }

        /// <summary>
        /// Sets up as a hi-res FLAC release
        /// </summary>
        public QobuzAlbumBuilder AsHiResFlac()
        {
            _maximumBitDepth = 24;
            _maximumSampleRate = 192000;
            return this;
        }

        /// <summary>
        /// Sets up as a CD quality FLAC release
        /// </summary>
        public QobuzAlbumBuilder AsCdQualityFlac()
        {
            _maximumBitDepth = 16;
            _maximumSampleRate = 44100;
            return this;
        }

        /// <summary>
        /// Sets up as an MP3 only release
        /// </summary>
        public QobuzAlbumBuilder AsMp3Only()
        {
            _maximumBitDepth = 16;
            _maximumSampleRate = 44100;
            return this;
        }

        /// <summary>
        /// Sets up as a classical album with appropriate metadata
        /// </summary>
        public QobuzAlbumBuilder AsClassical(string composer, string work)
        {
            _genre = new QobuzGenre { Name = "Classical" };
            _title = $"{composer}: {work}";
            _tracksCount = 8; // Typical classical work
            _durationSeconds = 2400; // 40 minutes
            return this;
        }

        /// <summary>
        /// Adds actual track data to the album
        /// </summary>
        public QobuzAlbumBuilder WithActualTracks(QobuzTrack[] tracks)
        {
            _tracksContainer = new QobuzTracksContainer
            {
                Items = tracks.ToList(),
                Total = tracks.Length
            };
            _tracksCount = tracks.Length;
            return this;
        }

        /// <summary>
        /// Sets up as a future release (early release testing)
        /// </summary>
        public QobuzAlbumBuilder AsFutureRelease(int daysInFuture = 30)
        {
            _releaseDate = DateTime.Now.AddDays(daysInFuture);
            return this;
        }

        /// <summary>
        /// Builds the QobuzAlbum with all configured properties
        /// </summary>
        public QobuzAlbum Build()
        {
            return new QobuzAlbum
            {
                Id = _id,
                Title = _title,
                Artist = _artist,
                Label = _label,
                Genre = _genre,
                ReleasedAtTimestamp = new DateTimeOffset(_releaseDate).ToUnixTimeSeconds(),
                TracksCount = _tracksCount,
                DurationSeconds = _durationSeconds,
                Streamable = _streamable,
                Purchasable = _purchasable,
                Sampleable = _sampleable,
                ParentalWarning = _parentalWarning,
                UPC = _upc,
                Image = _image,
                MaximumBitDepth = _maximumBitDepth,
                MaximumSampleRate = _maximumSampleRate,
                TracksContainer = _tracksContainer
            };
        }

        /// <summary>
        /// Builds multiple related albums (e.g., discography)
        /// </summary>
        public static QobuzAlbum[] BuildDiscography(string artistName, int albumCount)
        {
            var albums = new QobuzAlbum[albumCount];
            for (int i = 0; i < albumCount; i++)
            {
                albums[i] = New()
                    .WithId($"album_{artistName}_{i + 1}")
                    .WithTitle($"{artistName} Album {i + 1}")
                    .WithArtist(artistName)
                    .WithReleaseYear(2020 + i)
                    .Build();
            }
            return albums;
        }
    }
}