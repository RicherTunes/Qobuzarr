using System;
using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Unit.Services.Consolidated
{
    /// <summary>
    /// Simple tests for QobuzQualityManager focusing on basic functionality.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Component", "QobuzQualityManager")]
    public class QobuzQualityManagerSimpleTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<IQobuzLogger> _mockLogger;

        public QobuzQualityManagerSimpleTests(ITestOutputHelper output)
        {
            _output = output;
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockLogger = new Mock<IQobuzLogger>();
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Act
            var qualityManager = new QobuzQualityManager(_mockApiClient.Object, _mockLogger.Object);

            // Assert
            qualityManager.Should().NotBeNull();
            qualityManager.Should().BeAssignableTo<IQobuzQualityManager>();
            
            _output.WriteLine("✅ QobuzQualityManager constructed successfully");
        }

        [Fact]
        public void Constructor_WithNullApiClient_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(null, _mockLogger.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("apiClient");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(_mockApiClient.Object, null);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void QobuzQualityFormats_ShouldContainAllExpectedFormats()
        {
            // Act
            var formats = QobuzQualityManager.QobuzQualityFormats;

            // Assert
            formats.Should().NotBeNull();
            formats.Should().HaveCount(4); // MP3, FLAC CD, FLAC 96, FLAC 192
            formats.Should().ContainKeys(5, 6, 7, 27);
            
            // Verify format details
            formats[5].Name.Should().Be("MP3 320");
            formats[5].IsLossless.Should().BeFalse();
            
            formats[6].Name.Should().Be("FLAC CD");
            formats[6].IsLossless.Should().BeTrue();
            
            _output.WriteLine($"✅ QobuzQualityFormats contains {formats.Count} quality formats");
        }

        [Fact]
        public void QobuzQualityFormats_ShouldHaveCorrectPriorityOrder()
        {
            // Act
            var formats = QobuzQualityManager.QobuzQualityFormats.Values
                .OrderBy(f => f.Priority)
                .ToList();

            // Assert
            formats.Should().HaveCount(4);
            
            // Verify priority order (1 = lowest, 4 = highest)
            formats[0].Id.Should().Be(5);  // MP3 320 - Priority 1
            formats[1].Id.Should().Be(6);  // FLAC CD - Priority 2  
            formats[2].Id.Should().Be(7);  // FLAC Hi-Res 96 - Priority 3
            formats[3].Id.Should().Be(27); // FLAC Hi-Res 192 - Priority 4
            
            _output.WriteLine("✅ Quality formats have correct priority ordering");
        }

        [Fact]
        public void ServiceRegistration_ShouldCreateValidInstance()
        {
            // Act
            var qualityManager = ConsolidatedServiceRegistration.CreateQualityManager(
                _mockApiClient.Object, 
                _mockLogger.Object);

            // Assert
            qualityManager.Should().NotBeNull();
            qualityManager.Should().BeAssignableTo<IQobuzQualityManager>();
            qualityManager.Should().BeOfType<QobuzQualityManager>();
            
            _output.WriteLine("✅ ConsolidatedServiceRegistration.CreateQualityManager works correctly");
        }
    }
}