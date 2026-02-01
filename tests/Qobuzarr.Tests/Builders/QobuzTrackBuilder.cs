using System;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Builders
{
    /// <summary>
    /// Builder pattern for creating test QobuzTrack objects with fluent API
    /// </summary>
    public class QobuzTrackBuilder
    {
        private string _id = "123456";
        private string _title = "Test Track";
        private string _version = "";
        private int _trackNumber = 1;
        private int _discNumber = 1;
        private int _durationSeconds = 180;
        private bool _streamable = true;
        private QobuzArtist _performer = new QobuzArtist { Name = "Test Artist", Id = "artist123" };
        private QobuzComposer _composer = new QobuzComposer { Name = "Test Composer", Id = "composer123" };
        private string _isrc = "USRC12345678";
        private string _copyright = "2023 Test Label";
        private int _maximumBitDepth = 24;
        private double _maximumSampleRate = 192000;
        private bool _purchasable = true;
        private bool _previewable = true;
        private bool _sampleable = false;
        private bool _downloadable = true;
        private string _work = "";
        private string _part = "";

        /// <summary>
        /// Creates a new QobuzTrackBuilder with default test values
        /// </summary>
        public static QobuzTrackBuilder New() => new QobuzTrackBuilder();

        /// <summary>
        /// Sets the track ID
        /// </summary>
        public QobuzTrackBuilder WithId(string id)
        {
            _id = id;
            return this;
        }

        /// <summary>
        /// Sets the track title
        /// </summary>
        public QobuzTrackBuilder WithTitle(string title)
        {
            _title = title;
            return this;
        }

        /// <summary>
        /// Sets the track version (e.g., "Remastered", "Live")
        /// </summary>
        public QobuzTrackBuilder WithVersion(string version)
        {
            _version = version;
            return this;
        }

        /// <summary>
        /// Sets the track number
        /// </summary>
        public QobuzTrackBuilder WithTrackNumber(int trackNumber)
        {
            _trackNumber = trackNumber;
            return this;
        }

        /// <summary>
        /// Sets the disc number
        /// </summary>
        public QobuzTrackBuilder WithDiscNumber(int discNumber)
        {
            _discNumber = discNumber;
            return this;
        }

        /// <summary>
        /// Sets the duration in seconds
        /// </summary>
        public QobuzTrackBuilder WithDuration(int seconds)
        {
            _durationSeconds = seconds;
            return this;
        }

        /// <summary>
        /// Sets the performer/artist
        /// </summary>
        public QobuzTrackBuilder WithPerformer(string name, string id = null)
        {
            _performer = new QobuzArtist
            {
                Name = name,
                Id = id ?? $"artist_{name.ToLower().Replace(" ", "_")}"
            };
            return this;
        }

        /// <summary>
        /// Sets the composer
        /// </summary>
        public QobuzTrackBuilder WithComposer(string name, string id = null)
        {
            _composer = new QobuzComposer
            {
                Name = name,
                Id = id ?? $"composer_{name.ToLower().Replace(" ", "_")}"
            };
            return this;
        }

        /// <summary>
        /// Sets audio quality specifications
        /// </summary>
        public QobuzTrackBuilder WithQuality(int bitDepth, double sampleRate)
        {
            _maximumBitDepth = bitDepth;
            _maximumSampleRate = sampleRate;
            return this;
        }

        /// <summary>
        /// Marks the track as not streamable
        /// </summary>
        public QobuzTrackBuilder AsNotStreamable()
        {
            _streamable = false;
            return this;
        }

        /// <summary>
        /// Marks the track as not downloadable
        /// </summary>
        public QobuzTrackBuilder AsNotDownloadable()
        {
            _downloadable = false;
            return this;
        }

        /// <summary>
        /// Marks the track as a sample/preview only
        /// </summary>
        public QobuzTrackBuilder AsSampleOnly()
        {
            _sampleable = true;
            _downloadable = false;
            return this;
        }

        /// <summary>
        /// Sets up the track as a hi-res FLAC track
        /// </summary>
        public QobuzTrackBuilder AsHiResFlac()
        {
            _maximumBitDepth = 24;
            _maximumSampleRate = 192000;
            return this;
        }

        /// <summary>
        /// Sets up the track as a CD quality FLAC track
        /// </summary>
        public QobuzTrackBuilder AsCdQualityFlac()
        {
            _maximumBitDepth = 16;
            _maximumSampleRate = 44100;
            return this;
        }

        /// <summary>
        /// Sets up the track as an MP3 track
        /// </summary>
        public QobuzTrackBuilder AsMp3()
        {
            _maximumBitDepth = 16;
            _maximumSampleRate = 44100;
            return this;
        }

        /// <summary>
        /// Creates a classical music track with composer information
        /// </summary>
        public QobuzTrackBuilder AsClassical(string composer, string work, string part = "")
        {
            WithComposer(composer);
            _work = work;
            _part = part;
            return this;
        }

        /// <summary>
        /// Builds the QobuzTrack with all configured properties
        /// </summary>
        public QobuzTrack Build()
        {
            return new QobuzTrack
            {
                Id = _id,
                Title = _title,
                Version = _version,
                TrackNumber = _trackNumber,
                DiscNumber = _discNumber,
                DurationSeconds = _durationSeconds,
                Streamable = _streamable,
                Performer = _performer,
                Performers = _performer.Name,
                Composer = _composer,
                ISRC = _isrc,
                Copyright = _copyright,
                MaximumBitDepth = _maximumBitDepth,
                MaximumSampleRate = _maximumSampleRate,
                Purchasable = _purchasable,
                Previewable = _previewable,
                Sampleable = _sampleable,
                Downloadable = _downloadable,
                Work = _work,
                Part = _part
            };
        }

        /// <summary>
        /// Builds multiple tracks as part of an album
        /// </summary>
        public static QobuzTrack[] BuildAlbumTracks(int count, string albumId = "album123")
        {
            var tracks = new QobuzTrack[count];
            for (int i = 0; i < count; i++)
            {
                tracks[i] = New()
                    .WithId($"track_{albumId}_{i + 1}")
                    .WithTitle($"Track {i + 1}")
                    .WithTrackNumber(i + 1)
                    .Build();
            }
            return tracks;
        }
    }
}
