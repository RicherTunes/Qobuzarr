using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Validates plugin configuration for security best practices and potential vulnerabilities.
    /// Provides comprehensive security assessment of user-provided settings.
    /// </summary>
    /// <remarks>
    /// This validator was previously coupled to <c>SecureCredentialManager</c> for opaque
    /// "credential security" checks. As part of the token-storage migration to the common
    /// library, the small set of credential-format heuristics that the validator actually
    /// needs (<see cref="ValidateCredentialSecurity"/> and the env-var placeholder check)
    /// have been inlined here, eliminating the dependency on the deprecated SecureString-based
    /// credential manager.
    /// </remarks>
    public partial class SecurityConfigValidator
    {
        private readonly IQobuzLogger _logger;

        // Security patterns and validation rules (GeneratedRegex for SYSLIB1045)
        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
        private static partial Regex EmailPattern();

        [GeneratedRegex(@"^\d+$")]
        private static partial Regex NumericPattern();
        private static readonly string[] SuspiciousPatterns =
        {
            "javascript:", "<script", "<img", "onerror=", "onload=", "eval(", "document.", "window.",
            "../", "..\\", "/etc/", "c:\\", "%2e%2e", "0x",
            "union select", "' or ", "\" or ", "; drop ", "; delete "
        };

        public SecurityConfigValidator(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Performs comprehensive security validation of plugin settings.
        /// </summary>
        /// <param name="settings">Settings to validate</param>
        /// <returns>Security validation result with findings</returns>
        public SecurityValidationResult ValidateConfiguration(QobuzIndexerSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var result = new SecurityValidationResult();

            try
            {
                // Authentication security validation
                ValidateAuthenticationSecurity(settings, result);

                // Credential format validation
                ValidateCredentialFormats(settings, result);

                // App credentials validation
                ValidateAppCredentials(settings, result);

                // Configuration injection validation
                ValidateConfigurationInjection(settings, result);

                // Network security settings
                ValidateNetworkSecurity(settings, result);

                // Privacy and exposure validation
                ValidatePrivacySettings(settings, result);

                // Overall security score calculation
                CalculateSecurityScore(result);

                LogSecurityFindings(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Security validation failed");
                result.AddCriticalIssue("Security validation process failed",
                    "An error occurred during security validation. Please review configuration manually.");
                return result;
            }
        }

        private void ValidateAuthenticationSecurity(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            // Validate authentication method selection
            if (!settings.IsEmailAuth() && !settings.IsTokenAuth())
            {
                result.AddCriticalIssue("No authentication method configured",
                    "Configure either email/password or user ID/token authentication");
            }

            // Email authentication validation
            if (settings.IsEmailAuth())
            {
                if (!ValidateCredentialSecurity(settings.Email, "Email"))
                {
                    result.AddMajorIssue("Email credential security issue",
                        "Email appears to contain placeholder or invalid data");
                }

                if (!ValidateCredentialSecurity(settings.Password, "Password"))
                {
                    result.AddCriticalIssue("Password security issue",
                        "Password appears weak or contains placeholder data");
                }

                if (!EmailPattern().IsMatch(settings.Email ?? ""))
                {
                    result.AddMajorIssue("Invalid email format",
                        "Email address format appears invalid");
                }
            }

            // Token authentication validation
            if (settings.IsTokenAuth())
            {
                if (!ValidateCredentialSecurity(settings.UserId, "User ID"))
                {
                    result.AddMajorIssue("User ID security issue",
                        "User ID appears to contain placeholder or invalid data");
                }

                if (!ValidateCredentialSecurity(settings.AuthToken, "Auth Token"))
                {
                    result.AddCriticalIssue("Auth Token security issue",
                        "Auth token appears invalid or contains placeholder data");
                }
            }
        }

        private void ValidateCredentialFormats(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            // Check for obviously fake credentials
            var credentialsToCheck = new[]
            {
                (settings.Email, "Email"),
                (settings.Password, "Password"),
                (settings.UserId, "User ID"),
                (settings.AuthToken, "Auth Token"),
                (settings.AppId, "App ID"),
                (settings.AppSecret, "App Secret")
            };

            foreach (var (credential, name) in credentialsToCheck)
            {
                if (string.IsNullOrWhiteSpace(credential))
                    continue;

                if (ContainsSuspiciousPatterns(credential))
                {
                    result.AddCriticalIssue($"Suspicious {name} pattern detected",
                        $"{name} contains potentially malicious patterns");
                }

                if (IsObviouslyFakeCredential(credential))
                {
                    result.AddMajorIssue($"Potentially fake {name}",
                        $"{name} appears to be placeholder or test data");
                }
            }
        }

        private void ValidateAppCredentials(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            var hasAppId = !string.IsNullOrWhiteSpace(settings.AppId);
            var hasAppSecret = !string.IsNullOrWhiteSpace(settings.AppSecret);

            if (hasAppId && !hasAppSecret)
            {
                result.AddMajorIssue("Incomplete app credentials",
                    "App ID provided without App Secret. Both must be provided together or leave both empty for automatic credentials.");
            }
            else if (!hasAppId && hasAppSecret)
            {
                result.AddMajorIssue("Incomplete app credentials",
                    "App Secret provided without App ID. Both must be provided together or leave both empty for automatic credentials.");
            }
            else if (hasAppId && hasAppSecret)
            {
                // Validate App ID format (should be numeric)
                if (!NumericPattern().IsMatch(settings.AppId))
                {
                    result.AddMajorIssue("Invalid App ID format",
                        "App ID should be numeric");
                }

                // Check if App Secret looks reasonable (not too short, not placeholder)
                if (settings.AppSecret.Length < 10)
                {
                    result.AddMajorIssue("App Secret too short",
                        "App Secret appears too short to be valid");
                }

                result.AddInfoItem("Custom app credentials configured",
                    "Using user-provided App ID and Secret instead of automatic credentials");
            }
            else
            {
                result.AddInfoItem("Automatic app credentials",
                    "Will fetch App ID and Secret automatically from Qobuz web player");
            }
        }

        private void ValidateConfigurationInjection(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            var settingsToCheck = new Dictionary<string, string>
            {
                { nameof(settings.BaseUrl), settings.BaseUrl },
                { nameof(settings.CountryCode), settings.CountryCode },
                { nameof(settings.Email), settings.Email }
            };

            foreach (var setting in settingsToCheck)
            {
                if (string.IsNullOrWhiteSpace(setting.Value))
                    continue;

                if (ContainsSuspiciousPatterns(setting.Value))
                {
                    result.AddCriticalIssue($"Injection attempt detected in {setting.Key}",
                        $"{setting.Key} contains suspicious patterns that may indicate injection attempts");
                }
            }
        }

        private void ValidateNetworkSecurity(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            // Check base URL
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri))
                {
                    result.AddMajorIssue("Invalid base URL",
                        "Base URL format is invalid");
                }
                else
                {
                    if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddCriticalIssue("Insecure connection",
                            "Base URL should use HTTPS for secure communication");
                    }

                    if (!uri.Host.Equals("www.qobuz.com", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddMajorIssue("Non-standard API endpoint",
                            $"Base URL points to {uri.Host} instead of official Qobuz API");
                    }
                }
            }

            // Validate timeout settings
            if (settings.ConnectionTimeout < 5 || settings.ConnectionTimeout > 300)
            {
                result.AddMinorIssue("Connection timeout outside recommended range",
                    "Connection timeout should be between 5-300 seconds for optimal security/performance balance");
            }

            // Check rate limiting
            if (settings.ApiRateLimit > 300)
            {
                result.AddMinorIssue("High API rate limit",
                    "Very high rate limits may trigger API protection mechanisms");
            }
        }

        private void ValidatePrivacySettings(QobuzIndexerSettings settings, SecurityValidationResult result)
        {
            // Check for potentially privacy-exposing settings
            if (settings.SearchCacheDuration > 60)
            {
                result.AddMinorIssue("Long cache duration",
                    "Extended cache duration may retain user data longer than necessary");
            }

            // Validate country code for privacy implications
            if (!string.IsNullOrWhiteSpace(settings.CountryCode))
            {
                if (settings.CountryCode.Length != 2)
                {
                    result.AddMajorIssue("Invalid country code",
                        "Country code should be exactly 2 characters");
                }
            }
        }

        private void CalculateSecurityScore(SecurityValidationResult result)
        {
            int score = 100;

            score -= result.CriticalIssues.Count * 25;
            score -= result.MajorIssues.Count * 10;
            score -= result.MinorIssues.Count * 3;

            result.SecurityScore = Math.Max(0, score);

            if (result.SecurityScore >= 90)
                result.SecurityLevel = SecurityLevel.High;
            else if (result.SecurityScore >= 70)
                result.SecurityLevel = SecurityLevel.Medium;
            else if (result.SecurityScore >= 50)
                result.SecurityLevel = SecurityLevel.Low;
            else
                result.SecurityLevel = SecurityLevel.Critical;
        }

        private void LogSecurityFindings(SecurityValidationResult result)
        {
            _logger.Info("Security validation completed: Score={0}, Level={1}",
                result.SecurityScore, result.SecurityLevel);

            if (result.CriticalIssues.Any())
            {
                _logger.Warn("Critical security issues found: {0}", result.CriticalIssues.Count);
            }

            if (result.MajorIssues.Any())
            {
                _logger.Info("Major security issues found: {0}", result.MajorIssues.Count);
            }
        }

        private bool ContainsSuspiciousPatterns(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return SuspiciousPatterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsObviouslyFakeCredential(string credential)
        {
            if (string.IsNullOrWhiteSpace(credential))
                return false;

            var lower = credential.ToLowerInvariant();
            return lower.Contains("example") ||
                   lower.Contains("test") ||
                   lower.Contains("demo") ||
                   lower.Contains("placeholder") ||
                   lower.Contains("changeme") ||
                   lower.Contains("your_") ||
                   lower == "password" ||
                   lower == "admin" ||
                   lower == "user";
        }

        /// <summary>
        /// Performs heuristic safety checks on a credential value (not on storage). Returns
        /// false when the credential is empty, looks like a placeholder/env-var, or trips a
        /// hard-rejection pattern (file path, Windows env var, path traversal).
        /// </summary>
        /// <remarks>
        /// Inlined from the deprecated <c>SecureCredentialManager.ValidateCredentialSecurity</c>
        /// during the token-storage migration to the common library. The behavior is preserved
        /// to keep <see cref="SecurityConfigValidator"/> tests stable.
        /// </remarks>
        protected internal virtual bool ValidateCredentialSecurity(string credential, string credentialType)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                _logger.Warn("Empty {0} provided", credentialType);
                return false;
            }

            // Check for common security anti-patterns
            var lowerCredential = credential.ToLowerInvariant();

            if (lowerCredential.Contains("example") ||
                lowerCredential.Contains("test") ||
                lowerCredential.Contains("demo") ||
                lowerCredential.Contains("placeholder") ||
                lowerCredential.Contains("changeme") ||
                lowerCredential == "password" ||
                lowerCredential == "admin")
            {
                _logger.Warn("Potentially unsafe {0} detected - contains common placeholder values", credentialType);
                return false;
            }

            // Check minimum complexity for passwords
            if (credentialType.Contains("password", StringComparison.OrdinalIgnoreCase) && credential.Length < 8)
            {
                _logger.Warn("Password appears to be too short for security best practices");
                return false;
            }

            // HARD REJECT: Obvious environment variable placeholders.
            if (IsEnvironmentVariablePlaceholder(credential))
            {
                _logger.Warn("Credential appears to be an environment variable placeholder instead of actual credential");
                return false;
            }

            // HARD REJECT: Obvious file paths and Windows environment variables.
            if (credential.StartsWith('%') && credential.EndsWith('%') ||
                credential.Contains(":\\") ||
                credential.StartsWith("./", StringComparison.Ordinal) ||
                credential.Contains("../") ||
                credential.Contains("..\\") ||
                credential.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn("Credential appears to contain file path or environment variable instead of actual credential");
                return false;
            }

            return true;
        }

        private static bool IsEnvironmentVariablePlaceholder(string credential)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                return false;
            }

            if (credential.Length >= 3 &&
                credential.StartsWith('%') &&
                credential.EndsWith('%'))
            {
                return IsValidEnvVarName(credential.AsSpan(1, credential.Length - 2));
            }

            if (credential.Length >= 2 && credential.StartsWith('$'))
            {
                if (credential.Length >= 4 &&
                    credential.StartsWith("${", StringComparison.Ordinal) &&
                    credential.EndsWith('}'))
                {
                    return IsValidEnvVarName(credential.AsSpan(2, credential.Length - 3));
                }

                return IsValidEnvVarName(credential.AsSpan(1));
            }

            if (credential.StartsWith("%(", StringComparison.Ordinal) && credential.EndsWith(")s", StringComparison.Ordinal))
            {
                return IsValidEnvVarName(credential.AsSpan(2, credential.Length - 4));
            }

            return false;
        }

        private static bool IsValidEnvVarName(ReadOnlySpan<char> name)
        {
            if (name.IsEmpty)
            {
                return false;
            }

            var first = name[0];
            if (!(char.IsLetter(first) || first == '_'))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Result of security validation with findings categorized by severity.
    /// </summary>
    public class SecurityValidationResult
    {
        public List<SecurityIssue> CriticalIssues { get; } = new List<SecurityIssue>();
        public List<SecurityIssue> MajorIssues { get; } = new List<SecurityIssue>();
        public List<SecurityIssue> MinorIssues { get; } = new List<SecurityIssue>();
        public List<SecurityIssue> InfoItems { get; } = new List<SecurityIssue>();

        public int SecurityScore { get; set; } = 100;
        public SecurityLevel SecurityLevel { get; set; } = SecurityLevel.High;

        public bool HasCriticalIssues => CriticalIssues.Any();
        public bool HasSecurityIssues => CriticalIssues.Any() || MajorIssues.Any();
        public bool IsSecure => SecurityLevel >= SecurityLevel.Medium && !HasCriticalIssues;

        public void AddCriticalIssue(string title, string description)
        {
            CriticalIssues.Add(new SecurityIssue(title, description, SecurityIssueSeverity.Critical));
        }

        public void AddMajorIssue(string title, string description)
        {
            MajorIssues.Add(new SecurityIssue(title, description, SecurityIssueSeverity.Major));
        }

        public void AddMinorIssue(string title, string description)
        {
            MinorIssues.Add(new SecurityIssue(title, description, SecurityIssueSeverity.Minor));
        }

        public void AddInfoItem(string title, string description)
        {
            InfoItems.Add(new SecurityIssue(title, description, SecurityIssueSeverity.Info));
        }
    }

    public class SecurityIssue
    {
        public string Title { get; }
        public string Description { get; }
        public SecurityIssueSeverity Severity { get; }
        public DateTime DetectedAt { get; }

        public SecurityIssue(string title, string description, SecurityIssueSeverity severity)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Severity = severity;
            DetectedAt = DateTime.UtcNow;
        }
    }

    public enum SecurityLevel
    {
        Critical = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public enum SecurityIssueSeverity
    {
        Info,
        Minor,
        Major,
        Critical
    }
}
