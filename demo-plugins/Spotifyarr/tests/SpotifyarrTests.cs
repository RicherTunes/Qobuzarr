using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Common.Testing;
using Lidarr.Plugin.Spotifyarr.Settings;

namespace Lidarr.Plugin.Spotifyarr.Tests
{
    public class SpotifyarrTests
    {
        [Fact]
        public void Settings_ShouldValidate_WithSharedLibrary()
        {
            // Use shared library test utilities (50+ LOC saved)
            var settings = MockFactories.CreateMockSettings<SpotifySettings>();
            settings.SpotifyApiKey = "test_key_123";
            
            var isValid = settings.IsValid(out string error);
            isValid.Should().BeTrue();
        }

        [Fact]
        public void FileNaming_ShouldWork_WithSharedLibrary()
        {
            // Use shared library utilities for consistent testing
            var testAlbum = MockFactories.CreateMockAlbumWithTracks(10);
            var safeName = FileNameSanitizer.SanitizeFileName(testAlbum.Title);
            
            safeName.Should().NotBeNullOrEmpty();
            safeName.Should().NotContain('/');
        }

        // TODO: Add Spotify-specific tests
        // Use MockFactories for comprehensive test coverage!
    }
}
