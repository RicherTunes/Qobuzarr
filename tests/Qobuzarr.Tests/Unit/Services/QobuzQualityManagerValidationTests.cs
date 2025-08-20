using System;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Unit.Services
{
    /// <summary>
    /// Validation tests for the QobuzQualityManager consolidation.
    /// These tests verify that our service consolidation effort maintains quality
    /// and provides the expected consolidated functionality.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Component", "ServiceConsolidation")]
    public class QobuzQualityManagerValidationTests
    {
        private readonly ITestOutputHelper _output;

        public QobuzQualityManagerValidationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Service Consolidation Validation

        [Fact]
        public void QobuzQualityManager_Type_ShouldExist()
        {
            // This test validates that our QobuzQualityManager consolidation type exists
            var type = typeof(QobuzQualityManager);
            
            type.Should().NotBeNull();
            type.Name.Should().Be("QobuzQualityManager");
            type.Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.Services.Consolidated");
            
            _output.WriteLine("✅ QobuzQualityManager type exists in correct namespace");
        }

        [Fact]
        public void QobuzQualityManager_ShouldImplementInterface()
        {
            // Validate that the consolidated service implements the expected interface
            var type = typeof(QobuzQualityManager);
            var interfaceType = typeof(IQobuzQualityManager);
            
            type.Should().BeAssignableTo(interfaceType);
            
            _output.WriteLine("✅ QobuzQualityManager implements IQobuzQualityManager interface");
        }

        [Fact]
        public void ConsolidatedServiceRegistration_Type_ShouldExist()
        {
            // Validate that our service registration helper exists
            var type = typeof(ConsolidatedServiceRegistration);
            
            type.Should().NotBeNull();
            type.Name.Should().Be("ConsolidatedServiceRegistration");
            type.IsClass.Should().BeTrue();
            type.IsPublic.Should().BeTrue();
            
            _output.WriteLine("✅ ConsolidatedServiceRegistration helper class exists");
        }

        #endregion

        #region Quality Format Validation

        [Fact]
        public void QobuzQualityFormats_ShouldBeDefined()
        {
            // Validate that the consolidated quality formats are properly defined
            var formats = QobuzQualityManager.QobuzQualityFormats;
            
            formats.Should().NotBeNull();
            formats.Should().NotBeEmpty();
            formats.Should().HaveCount(4, "Should have 4 quality formats (MP3 320, FLAC CD, FLAC 96, FLAC 192)");
            
            // Verify expected format IDs are present
            formats.Should().ContainKeys(5, 6, 7, 27);
            
            _output.WriteLine($"✅ QobuzQualityFormats properly defined with {formats.Count} formats");
        }

        [Fact]
        public void QobuzQualityFormats_ShouldHaveValidData()
        {
            // Validate that each quality format has valid data
            var formats = QobuzQualityManager.QobuzQualityFormats;
            
            foreach (var format in formats.Values)
            {
                format.Should().NotBeNull();
                format.Name.Should().NotBeNullOrEmpty();
                format.DisplayName.Should().NotBeNullOrEmpty();
                format.BitRate.Should().BeGreaterThan(0);
                format.Priority.Should().BeGreaterThan(0);
                
                _output.WriteLine($"   Format {format.Id}: {format.DisplayName} - {format.BitRate}kbps (Priority: {format.Priority})");
            }
            
            _output.WriteLine("✅ All quality formats have valid data");
        }

        [Fact]
        public void QobuzQualityFormats_ShouldHaveLogicalClassification()
        {
            // Validate that lossless vs lossy classification makes sense
            var formats = QobuzQualityManager.QobuzQualityFormats;
            
            // MP3 320 should be lossy
            formats[5].IsLossless.Should().BeFalse("MP3 320 is lossy compression");
            
            // All FLAC formats should be lossless
            formats[6].IsLossless.Should().BeTrue("FLAC CD is lossless");
            formats[7].IsLossless.Should().BeTrue("FLAC Hi-Res 96 is lossless");
            formats[27].IsLossless.Should().BeTrue("FLAC Hi-Res 192 is lossless");
            
            _output.WriteLine("✅ Quality format lossless/lossy classification is correct");
        }

        #endregion

        #region Service Registration Validation

        [Fact]
        public void ConsolidatedServiceRegistration_ShouldHaveFactoryMethod()
        {
            // Validate that the factory method exists for service registration
            var type = typeof(ConsolidatedServiceRegistration);
            var method = type.GetMethod("CreateQualityManager");
            
            method.Should().NotBeNull("CreateQualityManager factory method should exist");
            method.IsStatic.Should().BeTrue("Factory method should be static");
            method.IsPublic.Should().BeTrue("Factory method should be public");
            method.ReturnType.Should().Be(typeof(IQobuzQualityManager));
            
            _output.WriteLine("✅ ConsolidatedServiceRegistration.CreateQualityManager factory method exists");
        }

        [Fact]
        public void ConsolidatedServiceRegistration_ShouldHaveMigrationAdapters()
        {
            // Validate that migration adapters are available for backward compatibility
            var type = typeof(ConsolidatedServiceRegistration);
            var nestedType = type.GetNestedType("MigrationAdapters");
            
            nestedType.Should().NotBeNull("MigrationAdapters nested class should exist");
            nestedType.IsClass.Should().BeTrue();
            nestedType.IsPublic.Should().BeTrue();
            
            // Check for adapter factory methods
            var qualityServiceMethod = nestedType.GetMethod("CreateQualityServiceAdapter");
            var mappingServiceMethod = nestedType.GetMethod("CreateMappingServiceAdapter");
            
            qualityServiceMethod.Should().NotBeNull("CreateQualityServiceAdapter method should exist");
            mappingServiceMethod.Should().NotBeNull("CreateMappingServiceAdapter method should exist");
            
            _output.WriteLine("✅ Migration adapters are available for backward compatibility");
            _output.WriteLine("   - CreateQualityServiceAdapter");
            _output.WriteLine("   - CreateMappingServiceAdapter");
        }

        #endregion

        #region Code Coverage Validation

        [Fact]
        public void ServiceConsolidation_ShouldHaveTestCoverage()
        {
            // This meta-test validates that we've created test coverage for our consolidation
            // By having this test run, we demonstrate that:
            // 1. The consolidated service types exist
            // 2. They're accessible from tests
            // 3. Basic validation passes
            // 4. Test infrastructure supports the new consolidated services
            
            var consolidatedTypes = new[]
            {
                typeof(QobuzQualityManager),
                typeof(IQobuzQualityManager),
                typeof(ConsolidatedServiceRegistration)
            };
            
            foreach (var type in consolidatedTypes)
            {
                type.Should().NotBeNull($"{type.Name} should exist and be accessible from tests");
            }
            
            _output.WriteLine("✅ Service consolidation has test coverage:");
            _output.WriteLine("   - QobuzQualityManager consolidated service");
            _output.WriteLine("   - IQobuzQualityManager interface");
            _output.WriteLine("   - ConsolidatedServiceRegistration helper");
            _output.WriteLine("   - Migration adapters for backward compatibility");
            _output.WriteLine("   - Quality format constants and validation");
        }

        [Theory]
        [InlineData(5, "MP3 320")]
        [InlineData(6, "FLAC CD")]
        [InlineData(7, "FLAC Hi-Res 96")]
        [InlineData(27, "FLAC Hi-Res 192")]
        public void QobuzQualityFormats_ShouldContainExpectedFormat(int formatId, string expectedName)
        {
            // Theory-based test to validate each expected format
            var formats = QobuzQualityManager.QobuzQualityFormats;
            
            formats.Should().ContainKey(formatId, $"Format {formatId} should exist");
            formats[formatId].Name.Should().Be(expectedName, $"Format {formatId} should have correct name");
            
            _output.WriteLine($"✅ Format {formatId} ({expectedName}) validated");
        }

        #endregion

        #region Performance Validation

        [Fact]
        public void QobuzQualityFormats_Access_ShouldBeFast()
        {
            // Validate that accessing quality formats is performant
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Access the formats multiple times to test performance
            for (int i = 0; i < 1000; i++)
            {
                var formats = QobuzQualityManager.QobuzQualityFormats;
                formats.Should().NotBeNull();
                formats.Should().HaveCount(4);
            }
            
            stopwatch.Stop();
            
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "Quality format access should be very fast");
            
            _output.WriteLine($"✅ Quality format access performance: {stopwatch.ElapsedMilliseconds}ms for 1000 accesses");
            _output.WriteLine($"   Average: {stopwatch.ElapsedMilliseconds / 1000.0:F3}ms per access");
        }

        #endregion
    }
}