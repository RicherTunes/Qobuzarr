using System;
using System.Collections.Generic;
using System.Linq;
using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Analyzes and categorizes download errors for better user understanding and troubleshooting.
    /// Extracted from DownloadCommand to follow Single Responsibility Principle.
    /// </summary>
    public class DownloadErrorAnalyzer
    {
        /// <summary>
        /// Categorizes failed downloads by error type for better reporting and troubleshooting.
        /// Groups similar errors together to help users understand common issues.
        /// </summary>
        /// <param name="failures">List of failed downloads with error messages.</param>
        /// <returns>Dictionary grouped by category with categorized failures.</returns>
        public Dictionary<string, List<DownloadResult>> CategorizeFailures(List<DownloadResult> failures)
        {
            var categories = new Dictionary<string, List<DownloadResult>>
            {
                ["Content Not Available"] = new(),
                ["Quality Issues"] = new(),
                ["Authentication Problems"] = new(),
                ["Network/API Errors"] = new(),
                ["File System Issues"] = new(),
                ["Unknown Issues"] = new()
            };

            foreach (var failure in failures)
            {
                var errorMsg = failure.ErrorMessage?.ToLower() ?? "";

                if (IsContentNotAvailableError(errorMsg))
                {
                    categories["Content Not Available"].Add(failure);
                }
                else if (IsQualityRelatedError(errorMsg))
                {
                    categories["Quality Issues"].Add(failure);
                }
                else if (IsAuthenticationError(errorMsg))
                {
                    categories["Authentication Problems"].Add(failure);
                }
                else if (IsNetworkApiError(errorMsg))
                {
                    categories["Network/API Errors"].Add(failure);
                }
                else if (IsFileSystemError(errorMsg))
                {
                    categories["File System Issues"].Add(failure);
                }
                else
                {
                    categories["Unknown Issues"].Add(failure);
                }
            }

            // Return only non-empty categories
            return categories.Where(kvp => kvp.Value.Any()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Gets the appropriate icon for a failure category for better visual organization.
        /// </summary>
        /// <param name="category">The failure category name.</param>
        /// <returns>Unicode emoji representing the category.</returns>
        public string GetCategoryIcon(string category)
        {
            return category switch
            {
                "Content Not Available" => "🚫",
                "Quality Issues" => "🎵",
                "Authentication Problems" => "🔑",
                "Network/API Errors" => "🌐",
                "File System Issues" => "💾",
                "Unknown Issues" => "❓",
                _ => "⚠️"
            };
        }

        /// <summary>
        /// Check if error indicates content not available (geo-blocking, preview only, etc.)
        /// </summary>
        private bool IsContentNotAvailableError(string errorMsg)
        {
            var indicators = new[]
            {
                "preview", "sample", "not available", "region", "country", "geoblocked",
                "no available quality", "track not found", "album not found"
            };
            return indicators.Any(indicator => errorMsg.Contains(indicator));
        }

        /// <summary>
        /// Check if error is quality-related (format issues, bitrate problems, etc.)
        /// </summary>
        private bool IsQualityRelatedError(string errorMsg)
        {
            var indicators = new[]
            {
                "quality", "format", "bitrate", "flac", "mp3", "hi-res"
            };
            return indicators.Any(indicator => errorMsg.Contains(indicator));
        }

        /// <summary>
        /// Check if error is authentication-related (session expired, invalid credentials, etc.)
        /// </summary>
        private bool IsAuthenticationError(string errorMsg)
        {
            var indicators = new[]
            {
                "auth", "token", "login", "credential", "session", "unauthorized", "forbidden"
            };
            return indicators.Any(indicator => errorMsg.Contains(indicator));
        }

        /// <summary>
        /// Check if error is network/API related (timeouts, connection issues, etc.)
        /// </summary>
        private bool IsNetworkApiError(string errorMsg)
        {
            var indicators = new[]
            {
                "network", "timeout", "connection", "api", "request", "response", "http", "ssl", "certificate"
            };
            return indicators.Any(indicator => errorMsg.Contains(indicator));
        }

        /// <summary>
        /// Check if error is file system related (permissions, disk space, etc.)
        /// </summary>
        private bool IsFileSystemError(string errorMsg)
        {
            var indicators = new[]
            {
                "file", "directory", "path", "disk", "space", "permission", "access", "write", "read"
            };
            return indicators.Any(indicator => errorMsg.Contains(indicator));
        }
    }
}
