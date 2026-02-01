using System;
using System.Collections.Concurrent;
using System.Security;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Secure credential management with memory protection and secure string handling.
    /// Provides additional security layers for sensitive authentication data.
    /// </summary>
    public class SecureCredentialManager : IDisposable
    {
        private readonly IQobuzLogger _logger;
        private readonly ConcurrentDictionary<string, SecureCredentialWrapper> _secureCredentials;
        private bool _disposed = false;

        public SecureCredentialManager(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _secureCredentials = new ConcurrentDictionary<string, SecureCredentialWrapper>();
        }

        /// <summary>
        /// Creates a SecureString from a regular string and clears the source.
        /// Provides protection against memory dumps and reduces credential exposure time.
        /// </summary>
        /// <param name="source">Source string to secure (will be cleared)</param>
        /// <returns>SecureString containing the credential data</returns>
        public SecureString CreateSecureString(string source)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            var secureString = new SecureString();

            try
            {
                foreach (char c in source)
                {
                    secureString.AppendChar(c);
                }

                secureString.MakeReadOnly();

                // Clear the source string from memory if possible
                ClearString(ref source);

                return secureString;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create secure string");
                secureString?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Converts a SecureString back to a regular string for API usage.
        /// Use sparingly and clear the result as soon as possible.
        /// </summary>
        /// <param name="secureString">The SecureString to convert</param>
        /// <returns>Plain text string (caller must clear after use)</returns>
        public string SecureStringToString(SecureString secureString)
        {
            if (secureString == null || secureString.Length == 0)
                return string.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
        }

        /// <summary>
        /// Securely clears a string from memory by nullifying the reference.
        /// Note: Actual memory clearing is limited in .NET due to string immutability.
        /// </summary>
        /// <param name="value">Reference to string to clear</param>
        public void ClearString(ref string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            try
            {
                // Clear the reference - actual memory clearing is limited in managed code
                // due to string immutability. The GC will handle memory reclamation
                // automatically and deterministically without blocking operations.
                value = null;

                // Note: Forcing GC.Collect() is an anti-pattern that causes:
                // - Performance degradation due to blocking all threads
                // - Promotion of objects to higher generations unnecessarily
                // - No guarantee of actual memory clearing for security
                // Instead, we rely on proper SecureString usage and disposal patterns
            }
            catch (Exception ex)
            {
                _logger.Debug("Could not securely clear string from memory: {0}", ex.Message);
                value = null;
            }
        }

        /// <summary>
        /// Validates that credentials are not obviously compromised or leaked.
        /// Performs basic security checks on credential format and content.
        /// </summary>
        /// <param name="credential">Credential to validate</param>
        /// <param name="credentialType">Type of credential for logging</param>
        /// <returns>True if credential passes basic security validation</returns>
        public virtual bool ValidateCredentialSecurity(string credential, string credentialType)
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
            if (credentialType.ToLowerInvariant().Contains("password") && credential.Length < 8)
            {
                _logger.Warn("Password appears to be too short for security best practices");
                return false;
            }

            // HARD REJECT: Obvious environment variable placeholders (plugin does not expand env vars)
            if (IsEnvironmentVariablePlaceholder(credential))
            {
                _logger.Warn("Credential appears to be an environment variable placeholder instead of actual credential");
                return false;
            }

            // HARD REJECT: Obvious file paths and Windows environment variables
            // These are clear indicators of misconfiguration (user pasted wrong value)
            // Note: We do NOT reject $ or / prefixes since opaque tokens can legitimately start with these
            if (credential.StartsWith("%") && credential.EndsWith("%") ||  // Windows env vars: %VAR%
                credential.Contains(":\\") ||           // Windows absolute paths: C:\...
                credential.StartsWith("./") ||          // Unix relative paths: ./path
                credential.Contains("../") ||           // Path traversal: ../../../etc/passwd
                credential.Contains("..\\") ||          // Windows path traversal: ..\..\..\
                credential.StartsWith("file:", StringComparison.OrdinalIgnoreCase))  // File URI
            {
                _logger.Warn("Credential appears to contain file path or environment variable instead of actual credential");
                return false;
            }

            // WARN ONLY: These patterns might appear in legitimate opaque tokens
            // Log a warning but don't reject - modern auth tokens can contain any characters
            // (including /, $, SQL keywords, etc.)
            var lowerCred = credential.ToLowerInvariant();
            bool hasSuspiciousPatterns =
                credential.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                credential.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                credential.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
                lowerCred.Contains("drop ") ||
                lowerCred.Contains("delete ") ||
                credential.Contains("--") ||
                credential.Contains("' or ", StringComparison.OrdinalIgnoreCase) ||
                credential.Contains("'='") ||
                // Warn on likely env var/path patterns but don't reject
                (credential.StartsWith("$") && credential.Contains("/")) ||  // Likely $HOME/path
                (credential.StartsWith("/") && credential.Contains("/etc"));  // Likely /etc/passwd

            if (hasSuspiciousPatterns)
            {
                _logger.Warn("Credential contains patterns that may indicate a misconfiguration. " +
                    "If this is a valid auth token, this warning can be ignored.");
                // Don't return false - opaque tokens might legitimately contain these patterns
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
                credential.StartsWith("%", StringComparison.Ordinal) &&
                credential.EndsWith("%", StringComparison.Ordinal))
            {
                return IsValidEnvVarName(credential.AsSpan(1, credential.Length - 2));
            }

            if (credential.Length >= 2 && credential.StartsWith("$", StringComparison.Ordinal))
            {
                if (credential.Length >= 4 &&
                    credential.StartsWith("${", StringComparison.Ordinal) &&
                    credential.EndsWith("}", StringComparison.Ordinal))
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

        /// <summary>
        /// Masks sensitive data for logging purposes while preserving some identifiable information.
        /// Shows first 2 and last 2 characters with asterisks in between.
        /// </summary>
        /// <param name="sensitive">Sensitive string to mask</param>
        /// <returns>Masked string safe for logging</returns>
        public string MaskSensitiveData(string sensitive)
        {
            if (string.IsNullOrWhiteSpace(sensitive))
                return "[empty]";

            if (sensitive.Length <= 4)
                return new string('*', sensitive.Length);

            return $"{sensitive.Substring(0, 2)}{"*".PadLeft(sensitive.Length - 4, '*')}{sensitive.Substring(sensitive.Length - 2)}";
        }

        /// <summary>
        /// Generates a secure hash of credentials for comparison purposes without storing plain text.
        /// Uses SHA-256 with salt for security (not MD5 which is used only for Qobuz API compatibility).
        /// </summary>
        /// <param name="credential">Credential to hash</param>
        /// <param name="salt">Salt value to prevent rainbow table attacks</param>
        /// <returns>Secure hash for storage/comparison</returns>
        public string GenerateSecureHash(string credential, byte[] salt = null)
        {
            if (string.IsNullOrEmpty(credential))
                throw new ArgumentException("Credential cannot be null or empty", nameof(credential));

            // Generate salt if not provided
            if (salt == null)
            {
                salt = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }
            }

            using (var sha256 = SHA256.Create())
            {
                var credentialBytes = Encoding.UTF8.GetBytes(credential);
                var saltedCredential = new byte[credentialBytes.Length + salt.Length];

                Array.Copy(credentialBytes, 0, saltedCredential, 0, credentialBytes.Length);
                Array.Copy(salt, 0, saltedCredential, credentialBytes.Length, salt.Length);

                var hash = sha256.ComputeHash(saltedCredential);

                // Clear intermediate arrays
                Array.Clear(credentialBytes, 0, credentialBytes.Length);
                Array.Clear(saltedCredential, 0, saltedCredential.Length);

                // Combine salt and hash for storage
                var result = new byte[salt.Length + hash.Length];
                Array.Copy(salt, 0, result, 0, salt.Length);
                Array.Copy(hash, 0, result, salt.Length, hash.Length);

                return Convert.ToBase64String(result);
            }
        }

        /// <summary>
        /// Verifies a credential against a previously generated secure hash.
        /// </summary>
        /// <param name="credential">Credential to verify</param>
        /// <param name="storedHash">Previously generated secure hash</param>
        /// <returns>True if credential matches the stored hash</returns>
        public bool VerifySecureHash(string credential, string storedHash)
        {
            try
            {
                var storedBytes = Convert.FromBase64String(storedHash);
                if (storedBytes.Length < 64) // 32 bytes salt + 32 bytes SHA-256 hash minimum
                    return false;

                var salt = new byte[32];
                Array.Copy(storedBytes, 0, salt, 0, 32);

                var computedHash = GenerateSecureHash(credential, salt);
                return storedHash == computedHash;
            }
            catch (Exception ex)
            {
                _logger.Debug("Hash verification failed: {0}", ex.Message);
                return false;
            }
        }

        #region Enhanced Concurrent Credential Storage

        /// <summary>
        /// Stores a credential securely in memory using SecureString
        /// </summary>
        /// <param name="key">Credential identifier</param>
        /// <param name="credential">Credential value</param>
        public void StoreSecureCredential(string key, string credential)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecureCredentialManager));

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Credential key cannot be null or empty", nameof(key));

            // Remove existing credential if present
            if (_secureCredentials.TryRemove(key, out var existing))
            {
                existing.Dispose();
            }

            // Store new secure credential
            var secureWrapper = new SecureCredentialWrapper(credential);
            _secureCredentials.TryAdd(key, secureWrapper);

            _logger.Debug("Secure credential stored for key: {0}", key);
        }

        /// <summary>
        /// Uses a stored secure credential with automatic cleanup
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="key">Credential identifier</param>
        /// <param name="func">Function to execute with credential</param>
        /// <returns>Result of the function</returns>
        public T UseSecureCredential<T>(string key, Func<string, T> func)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecureCredentialManager));

            if (!_secureCredentials.TryGetValue(key, out var wrapper))
                throw new InvalidOperationException($"No secure credential found for key: {key}");

            return wrapper.UseCredential(func);
        }

        /// <summary>
        /// Uses a stored secure credential with automatic cleanup (async version)
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="key">Credential identifier</param>
        /// <param name="func">Async function to execute with credential</param>
        /// <returns>Result of the function</returns>
        public async Task<T> UseSecureCredentialAsync<T>(string key, Func<string, Task<T>> func)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecureCredentialManager));

            if (!_secureCredentials.TryGetValue(key, out var wrapper))
                throw new InvalidOperationException($"No secure credential found for key: {key}");

            return await wrapper.UseCredentialAsync(func).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a secure credential is stored for the given key
        /// </summary>
        /// <param name="key">Credential identifier</param>
        /// <returns>True if credential exists</returns>
        public bool HasSecureCredential(string key)
        {
            if (_disposed)
                return false;

            return _secureCredentials.ContainsKey(key);
        }

        /// <summary>
        /// Removes a stored secure credential
        /// </summary>
        /// <param name="key">Credential identifier</param>
        /// <returns>True if credential was removed</returns>
        public bool RemoveSecureCredential(string key)
        {
            if (_disposed)
                return false;

            if (_secureCredentials.TryRemove(key, out var wrapper))
            {
                wrapper.Dispose();
                _logger.Debug("Secure credential removed for key: {0}", key);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all stored secure credentials
        /// </summary>
        public void ClearAllSecureCredentials()
        {
            if (_disposed)
                return;

            foreach (var wrapper in _secureCredentials.Values)
            {
                wrapper.Dispose();
            }

            _secureCredentials.Clear();
            _logger.Debug("All secure credentials cleared");
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clear all secure credentials
                ClearAllSecureCredentials();
                _disposed = true;
            }
        }
    }
}
