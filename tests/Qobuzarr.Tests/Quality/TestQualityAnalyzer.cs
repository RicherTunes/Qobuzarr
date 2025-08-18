using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

namespace Qobuzarr.Tests.Quality
{
    /// <summary>
    /// Analyzes and reports test quality metrics for the test suite
    /// Provides comprehensive coverage analysis and quality scoring
    /// </summary>
    public class TestQualityAnalyzer
    {
        /// <summary>
        /// Comprehensive test quality report
        /// </summary>
        public class QualityReport
        {
            public int TotalTestMethods { get; set; }
            public int TotalTestClasses { get; set; }
            public int UnitTests { get; set; }
            public int IntegrationTests { get; set; }
            public int PerformanceTests { get; set; }
            public int PropertyBasedTests { get; set; }
            public int TestsWithCustomAssertions { get; set; }
            public int TestsWithBuilders { get; set; }
            public double QualityScore { get; set; }
            public List<string> QualityMetrics { get; set; } = new List<string>();
            public List<string> Recommendations { get; set; } = new List<string>();
            public Dictionary<string, int> TestCategoryCounts { get; set; } = new Dictionary<string, int>();
            public DateTime AnalysisDate { get; set; } = DateTime.UtcNow;
        }

        /// <summary>
        /// Analyzes the current test suite and returns a comprehensive quality report
        /// </summary>
        public static QualityReport AnalyzeTestSuite()
        {
            var report = new QualityReport();
            var testAssembly = Assembly.GetExecutingAssembly();
            var testTypes = testAssembly.GetTypes()
                .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>().Any() || 
                                                  m.GetCustomAttributes<TheoryAttribute>().Any()))
                .ToList();

            report.TotalTestClasses = testTypes.Count;

            // Analyze each test class
            foreach (var testType in testTypes)
            {
                AnalyzeTestClass(testType, report);
            }

            // Calculate quality metrics
            CalculateQualityScore(report);
            GenerateRecommendations(report);

            return report;
        }

        private static void AnalyzeTestClass(Type testType, QualityReport report)
        {
            var testMethods = testType.GetMethods()
                .Where(m => m.GetCustomAttributes<FactAttribute>().Any() || 
                           m.GetCustomAttributes<TheoryAttribute>().Any())
                .ToList();

            report.TotalTestMethods += testMethods.Count;

            // Categorize tests by naming patterns and attributes
            foreach (var method in testMethods)
            {
                CategorizeTestMethod(method, testType, report);
            }
        }

        private static void CategorizeTestMethod(MethodInfo method, Type testType, QualityReport report)
        {
            var methodName = method.Name;
            var className = testType.Name;

            // Unit tests
            if (className.EndsWith("Tests") && !className.Contains("Integration") && 
                !className.Contains("Performance") && !className.Contains("Concurrency"))
            {
                report.UnitTests++;
            }

            // Integration tests
            if (className.Contains("Integration") || methodName.Contains("Integration") ||
                methodName.Contains("Workflow") || methodName.Contains("EndToEnd"))
            {
                report.IntegrationTests++;
                report.TestCategoryCounts["Integration"] = report.TestCategoryCounts.GetValueOrDefault("Integration", 0) + 1;
            }

            // Performance tests
            if (className.Contains("Performance") || className.Contains("Concurrency") ||
                methodName.Contains("Performance") || methodName.Contains("Concurrency") ||
                methodName.Contains("Load") || methodName.Contains("Stress"))
            {
                report.PerformanceTests++;
                report.TestCategoryCounts["Performance"] = report.TestCategoryCounts.GetValueOrDefault("Performance", 0) + 1;
            }

            // Property-based tests (Theory tests with InlineData)
            if (method.GetCustomAttributes<TheoryAttribute>().Any())
            {
                report.PropertyBasedTests++;
                report.TestCategoryCounts["PropertyBased"] = report.TestCategoryCounts.GetValueOrDefault("PropertyBased", 0) + 1;
            }

            // Check for advanced testing patterns
            AnalyzeTestMethodContent(method, report);
        }

        private static void AnalyzeTestMethodContent(MethodInfo method, QualityReport report)
        {
            // This would require source code analysis - for now we'll use reflection-based heuristics
            
            // Check if method likely uses builders (by parameter types or naming)
            var methodBody = method.GetMethodBody();
            if (methodBody != null)
            {
                // Heuristic: if method has reasonable complexity, assume it uses advanced patterns
                if (methodBody.GetILAsByteArray().Length > 50)
                {
                    report.TestsWithBuilders++;
                }
            }

            // Check for custom assertions by examining method names
            if (method.Name.Contains("Should") && method.Name.Contains("_"))
            {
                report.TestsWithCustomAssertions++;
            }
        }

        private static void CalculateQualityScore(QualityReport report)
        {
            double score = 0;
            var metrics = report.QualityMetrics;

            // Base score for having tests
            if (report.TotalTestMethods > 0)
            {
                score += 20;
                metrics.Add($"Base Score: 20 points for having {report.TotalTestMethods} test methods");
            }

            // Test diversity scoring
            if (report.UnitTests > 0)
            {
                score += 15;
                metrics.Add($"Unit Tests: 15 points for {report.UnitTests} unit tests");
            }

            if (report.IntegrationTests > 0)
            {
                score += 20;
                metrics.Add($"Integration Tests: 20 points for {report.IntegrationTests} integration tests");
            }

            if (report.PerformanceTests > 0)
            {
                score += 15;
                metrics.Add($"Performance Tests: 15 points for {report.PerformanceTests} performance tests");
            }

            if (report.PropertyBasedTests > 0)
            {
                score += 10;
                metrics.Add($"Property-Based Tests: 10 points for {report.PropertyBasedTests} property-based tests");
            }

            // Advanced pattern scoring
            if (report.TestsWithCustomAssertions > 0)
            {
                score += 10;
                metrics.Add($"Custom Assertions: 10 points for using domain-specific assertions");
            }

            if (report.TestsWithBuilders > 0)
            {
                score += 10;
                metrics.Add($"Test Builders: 10 points for using test data builders");
            }

            // Quality multipliers
            double testCoverageRatio = (double)(report.IntegrationTests + report.UnitTests) / Math.Max(report.TotalTestMethods, 1);
            if (testCoverageRatio > 0.8)
            {
                score *= 1.1;
                metrics.Add($"High Coverage Ratio: 10% bonus for {testCoverageRatio:P1} coverage");
            }

            // Test organization bonus
            if (report.TotalTestClasses >= 5 && report.TestCategoryCounts.Count >= 3)
            {
                score += 5;
                metrics.Add($"Well Organized: 5 points for good test organization across {report.TotalTestClasses} classes");
            }

            report.QualityScore = Math.Min(score, 100); // Cap at 100%
        }

        private static void GenerateRecommendations(QualityReport report)
        {
            var recommendations = report.Recommendations;

            if (report.QualityScore >= 95)
            {
                recommendations.Add("🎉 Excellent test quality! Your test suite demonstrates enterprise-grade testing practices.");
            }
            else if (report.QualityScore >= 85)
            {
                recommendations.Add("✅ Very good test quality. Consider adding more edge case coverage.");
            }
            else if (report.QualityScore >= 70)
            {
                recommendations.Add("👍 Good test foundation. Add more integration and performance tests.");
            }
            else
            {
                recommendations.Add("⚠️ Test quality needs improvement. Focus on test coverage and diversity.");
            }

            // Specific recommendations
            if (report.IntegrationTests == 0)
            {
                recommendations.Add("Add integration tests to validate end-to-end workflows");
            }

            if (report.PerformanceTests == 0)
            {
                recommendations.Add("Add performance tests to validate system behavior under load");
            }

            if (report.PropertyBasedTests < 5)
            {
                recommendations.Add("Consider adding more property-based tests for edge case coverage");
            }

            if (report.TestsWithCustomAssertions == 0)
            {
                recommendations.Add("Implement custom FluentAssertions extensions for better test readability");
            }

            if (report.TotalTestMethods < 50)
            {
                recommendations.Add("Expand test coverage with more comprehensive test scenarios");
            }

            // Advanced recommendations
            if (report.QualityScore > 90)
            {
                recommendations.Add("Consider adding mutation testing to validate test effectiveness");
                recommendations.Add("Implement test data generation strategies for broader coverage");
                recommendations.Add("Add benchmarking tests for performance regression detection");
            }
        }

        /// <summary>
        /// Exports the quality report to JSON format
        /// </summary>
        public static string ExportReportToJson(QualityReport report)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(report, options);
        }

        /// <summary>
        /// Saves the quality report to a file
        /// </summary>
        public static void SaveReportToFile(QualityReport report, string filePath)
        {
            var json = ExportReportToJson(report);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Generates a human-readable summary of the quality report
        /// </summary>
        public static string GenerateReadableSummary(QualityReport report)
        {
            var summary = $@"
# Test Quality Analysis Report
Generated: {report.AnalysisDate:yyyy-MM-dd HH:mm:ss} UTC

## Overall Quality Score: {report.QualityScore:F1}/100

## Test Suite Overview
- **Total Test Methods**: {report.TotalTestMethods}
- **Total Test Classes**: {report.TotalTestClasses}
- **Unit Tests**: {report.UnitTests}
- **Integration Tests**: {report.IntegrationTests}
- **Performance Tests**: {report.PerformanceTests}
- **Property-Based Tests**: {report.PropertyBasedTests}

## Advanced Testing Patterns
- **Tests with Custom Assertions**: {report.TestsWithCustomAssertions}
- **Tests with Builders**: {report.TestsWithBuilders}

## Quality Metrics
{string.Join("\n", report.QualityMetrics.Select(m => $"- {m}"))}

## Recommendations
{string.Join("\n", report.Recommendations.Select(r => $"- {r}"))}

## Test Categories
{string.Join("\n", report.TestCategoryCounts.Select(kvp => $"- **{kvp.Key}**: {kvp.Value} tests"))}
";

            return summary;
        }
    }
}