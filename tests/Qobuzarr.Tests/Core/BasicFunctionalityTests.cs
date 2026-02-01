using System;
using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Core
{
    /// <summary>
    /// Core functionality tests that don't depend on Lidarr interfaces
    /// These tests verify our business logic works independently
    /// </summary>
    public class BasicFunctionalityTests
    {
        [Fact]
        public void QobuzCredentials_Validation_ShouldWorkCorrectly()
        {
            // Valid email credentials
            var emailCreds = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            emailCreds.IsValid().Should().BeTrue();
            emailCreds.IsEmailAuth().Should().BeTrue();
            emailCreds.IsTokenAuth().Should().BeFalse();
        }

        [Fact]
        public void QobuzCredentials_TokenValidation_ShouldWorkCorrectly()
        {
            // Valid token credentials
            var tokenCreds = new QobuzCredentials
            {
                UserId = "12345678",
                AuthToken = "sample_token_123",
                AppId = "test_app_id"
            };

            tokenCreds.IsValid().Should().BeTrue();
            tokenCreds.IsEmailAuth().Should().BeFalse();
            tokenCreds.IsTokenAuth().Should().BeTrue();
        }

        [Fact]
        public void QobuzSession_Validation_ShouldWorkCorrectly()
        {
            // Valid session
            var session = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "token123",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            session.IsValid().Should().BeTrue();
            session.NeedsRefresh().Should().BeFalse();
        }

        [Fact]
        public void QobuzSession_Expiration_ShouldWorkCorrectly()
        {
            // Expired session
            var expiredSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "token123",
                ExpiresAt = DateTime.UtcNow.AddHours(-1)
            };

            expiredSession.IsValid().Should().BeFalse();
            expiredSession.NeedsRefresh().Should().BeTrue();
        }

        [Fact]
        public void QobuzAlbum_Deserialization_ShouldWorkCorrectly()
        {
            // Test album deserialization
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            album.Should().NotBeNull();
            album.Id.Should().Be("0060254788359");
            album.Title.Should().Be("Random Access Memories");
            album.Artist.Should().NotBeNull();
            album.Artist.Name.Should().Be("Daft Punk");
            album.TracksCount.Should().Be(13);
            album.Duration.TotalSeconds.Should().BeApproximately(4578, 1);
        }

        [Fact]
        public void QobuzAlbum_BusinessLogic_ShouldWorkCorrectly()
        {
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Test business logic methods using actual available methods
            album.GetFullTitle().Should().Be("Random Access Memories");
            album.GetSafeFolderName().Should().NotBeNullOrEmpty();
            album.GetArtistName().Should().Be("Daft Punk");
            album.Streamable.Should().BeTrue();
        }

        [Fact]
        public void QobuzTrack_BusinessLogic_ShouldWorkCorrectly()
        {
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            var track = album.GetTracks()[0];

            // Test track business logic using actual available methods
            track.GetFullTitle().Should().Be("Give Life Back to Music");
            track.GetSafeFileName().Should().NotBeNullOrEmpty();
            track.Duration.TotalSeconds.Should().BeApproximately(274, 1);
        }

        [Fact]
        public void QobuzAlbum_SizeEstimation_ShouldWorkCorrectly()
        {
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Test size estimation
            var mp3Size = album.GetEstimatedTotalSize(5); // MP3 320kbps
            var flacSize = album.GetEstimatedTotalSize(6); // FLAC CD

            mp3Size.Should().BeGreaterThan(0);
            flacSize.Should().BeGreaterThan(mp3Size); // FLAC should be larger than MP3

            // Reasonable size ranges for a 4578 seconds album (13 tracks)
            mp3Size.Should().BeLessThan(500_000_000); // < 500MB for MP3
            flacSize.Should().BeGreaterThan(30_000_000); // > 30MB for FLAC (more realistic)
            flacSize.Should().BeLessThan(200_000_000); // < 200MB for FLAC (reasonable upper bound)
        }

        [Fact]
        public void StringSanitization_ShouldWorkCorrectly()
        {
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = "Test: Album? <Name> \"Special\" /Path\\File",
                Artist = new QobuzArtist { Name = "Test Artist" }
            };

            var safeTitle = album.GetSafeFolderName();

            safeTitle.Should().NotContain(":");
            safeTitle.Should().NotContain("?");
            safeTitle.Should().NotContain("<");
            safeTitle.Should().NotContain(">");
            safeTitle.Should().NotContain("\"");
            safeTitle.Should().NotContain("/");
            safeTitle.Should().NotContain("\\");
            // GetSafeFolderName returns "Artist - Title" format with illegal chars replaced by _
            safeTitle.Should().Be("Test Artist - Test_ Album_ _Name_ _Special_ _Path_File");
        }

        [Fact]
        public void QobuzCredentials_Security_ShouldNotExposeSecrets()
        {
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "secretpassword",
                AuthToken = "secrettoken"
            };

            var stringRepresentation = credentials.ToString();

            stringRepresentation.Should().NotContain("secretpassword");
            stringRepresentation.Should().NotContain("secrettoken");
            // ToString() returns default class name, which is safe but doesn't expose email
            stringRepresentation.Should().Be("Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials");
        }

        // File extension mapping is now handled by QobuzStreamResponse
        // [Theory]
        // [InlineData(5, "mp3")]  // MP3 320kbps
        // [InlineData(6, "flac")] // FLAC CD
        // [InlineData(7, "flac")] // FLAC Hi-Res 24/96
        // [InlineData(27, "flac")] // FLAC Hi-Res 24/192
        // public void FormatMapping_ShouldReturnCorrectExtensions(int formatId, string expectedExtension)
        // {
        //     var streamResponse = new QobuzStreamResponse { FormatId = formatId };
        //     var extension = streamResponse.GetFileExtension();
        //     extension.Should().Be($".{expectedExtension}");
        // }

        [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("user+tag@domain.co.uk", true)]
        [InlineData("invalid.email", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void EmailValidation_ShouldWorkCorrectly(string email, bool expectedValid)
        {
            var credentials = new QobuzCredentials { Email = email, MD5Password = "test", AppId = "test" };
            var isValid = credentials.IsValid() && credentials.IsEmailAuth();
            isValid.Should().Be(expectedValid);
        }

        [Fact]
        public void EmailValidation_AtDomainCom_ShouldBeInvalid()
        {
            // Special test case for @domain.com which might be considered valid by EmailAddressAttribute
            // but should be invalid for our business logic
            var credentials = new QobuzCredentials { Email = "@domain.com", MD5Password = "test", AppId = "test" };
            var isValid = credentials.IsValid() && credentials.IsEmailAuth();

            // This email should be invalid as it lacks a username part
            isValid.Should().BeFalse();
        }
    }
}
