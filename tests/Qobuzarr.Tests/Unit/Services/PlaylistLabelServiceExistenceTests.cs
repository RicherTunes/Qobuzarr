using System;
using System.Reflection;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Services;
// DISABLED: PlaylistDownloadService and LabelDownloadService have been removed
// using Lidarr.Plugin.Qobuzarr.Download.Services;

namespace Qobuzarr.Tests.Unit.Services
{
    /// <summary>
    /// DISABLED: Tests to verify the new playlist and label functionality exists in the services
    /// PlaylistDownloadService and LabelDownloadService have been removed - functionality consolidated
    /// </summary>
    /*
    public class PlaylistLabelServiceExistenceTests
    {
        [Fact]
        public void QobuzSearchService_ShouldHavePlaylistMethods()
        {
            // Arrange
            var searchServiceType = typeof(QobuzSearchService);
            
            // Act & Assert - Check for playlist search methods
            searchServiceType.GetMethod("SearchPlaylistsAsync").Should().NotBeNull("SearchPlaylistsAsync method should exist");
            searchServiceType.GetMethod("GetPlaylistAsync").Should().NotBeNull("GetPlaylistAsync method should exist");
            searchServiceType.GetMethod("GetPlaylistTracksAsync").Should().NotBeNull("GetPlaylistTracksAsync method should exist");
        }

        [Fact]
        public void QobuzSearchService_ShouldHaveLabelMethods()
        {
            // Arrange
            var searchServiceType = typeof(QobuzSearchService);
            
            // Act & Assert - Check for label search methods
            searchServiceType.GetMethod("SearchLabelsAsync").Should().NotBeNull("SearchLabelsAsync method should exist");
            searchServiceType.GetMethod("GetLabelAsync").Should().NotBeNull("GetLabelAsync method should exist");
            searchServiceType.GetMethod("GetLabelAlbumsAsync").Should().NotBeNull("GetLabelAlbumsAsync method should exist");
        }

        [Fact]
        public void PlaylistDownloadService_ShouldExist()
        {
            // Arrange & Act
            var playlistDownloadServiceType = typeof(PlaylistDownloadService);
            
            // Assert
            playlistDownloadServiceType.Should().NotBeNull("PlaylistDownloadService should exist");
            playlistDownloadServiceType.GetMethod("DownloadPlaylistAsync").Should().NotBeNull("DownloadPlaylistAsync method should exist");
        }

        [Fact]
        public void LabelDownloadService_ShouldExist()
        {
            // Arrange & Act
            var labelDownloadServiceType = typeof(LabelDownloadService);
            
            // Assert
            labelDownloadServiceType.Should().NotBeNull("LabelDownloadService should exist");
            labelDownloadServiceType.GetMethod("DownloadLabelAsync").Should().NotBeNull("DownloadLabelAsync method should exist");
        }

        [Fact]
        public void QobuzSearchService_PlaylistMethods_ShouldHaveCorrectReturnTypes()
        {
            // Arrange
            var searchServiceType = typeof(QobuzSearchService);
            
            // Act
            var searchPlaylistsMethod = searchServiceType.GetMethod("SearchPlaylistsAsync");
            var getPlaylistMethod = searchServiceType.GetMethod("GetPlaylistAsync");
            var getPlaylistTracksMethod = searchServiceType.GetMethod("GetPlaylistTracksAsync");
            
            // Assert
            searchPlaylistsMethod.ReturnType.Name.Should().Contain("Task", "SearchPlaylistsAsync should return a Task");
            getPlaylistMethod.ReturnType.Name.Should().Contain("Task", "GetPlaylistAsync should return a Task");
            getPlaylistTracksMethod.ReturnType.Name.Should().Contain("Task", "GetPlaylistTracksAsync should return a Task");
        }

        [Fact]
        public void QobuzSearchService_LabelMethods_ShouldHaveCorrectReturnTypes()
        {
            // Arrange
            var searchServiceType = typeof(QobuzSearchService);
            
            // Act
            var searchLabelsMethod = searchServiceType.GetMethod("SearchLabelsAsync");
            var getLabelMethod = searchServiceType.GetMethod("GetLabelAsync");
            var getLabelAlbumsMethod = searchServiceType.GetMethod("GetLabelAlbumsAsync");
            
            // Assert
            searchLabelsMethod.ReturnType.Name.Should().Contain("Task", "SearchLabelsAsync should return a Task");
            getLabelMethod.ReturnType.Name.Should().Contain("Task", "GetLabelAsync should return a Task");
            getLabelAlbumsMethod.ReturnType.Name.Should().Contain("Task", "GetLabelAlbumsAsync should return a Task");
        }

        [Fact]
        public void PlaylistDownloadService_DownloadMethod_ShouldHaveCorrectSignature()
        {
            // Arrange
            var playlistDownloadServiceType = typeof(PlaylistDownloadService);
            
            // Act
            var downloadPlaylistMethod = playlistDownloadServiceType.GetMethod("DownloadPlaylistAsync");
            var parameters = downloadPlaylistMethod.GetParameters();
            
            // Assert
            parameters.Should().NotBeEmpty("DownloadPlaylistAsync should have parameters");
            parameters[0].ParameterType.Should().Be<string>("First parameter should be string (playlistId)");
            parameters[1].ParameterType.Should().Be<string>("Second parameter should be string (outputPath)");
        }

        [Fact]
        public void LabelDownloadService_DownloadMethod_ShouldHaveCorrectSignature()
        {
            // Arrange
            var labelDownloadServiceType = typeof(LabelDownloadService);
            
            // Act
            var downloadLabelMethod = labelDownloadServiceType.GetMethod("DownloadLabelAsync");
            var parameters = downloadLabelMethod.GetParameters();
            
            // Assert
            parameters.Should().NotBeEmpty("DownloadLabelAsync should have parameters");
            parameters[0].ParameterType.Should().Be<string>("First parameter should be string (labelId)");
            parameters[1].ParameterType.Should().Be<string>("Second parameter should be string (outputPath)");
        }

        [Fact]
        public void NewPlaylistLabelServices_ShouldBeInCorrectNamespaces()
        {
            // Arrange & Act
            var playlistDownloadServiceType = typeof(PlaylistDownloadService);
            var labelDownloadServiceType = typeof(LabelDownloadService);
            
            // Assert
            playlistDownloadServiceType.Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.Download.Services", 
                "PlaylistDownloadService should be in correct namespace");
            labelDownloadServiceType.Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.Download.Services", 
                "LabelDownloadService should be in correct namespace");
        }

        [Fact]
        public void PlaylistLabelModels_ShouldExist()
        {
            // Test that the playlist and label models exist (they were created in previous implementation)
            var playlistType = typeof(Lidarr.Plugin.Qobuzarr.Models.QobuzPlaylist);
            var labelType = typeof(Lidarr.Plugin.Qobuzarr.Models.QobuzLabel);
            
            playlistType.Should().NotBeNull("QobuzPlaylist model should exist");
            labelType.Should().NotBeNull("QobuzLabel model should exist");
        }
    }
    */
}