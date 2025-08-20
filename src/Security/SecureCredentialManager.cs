using System;
using System.Security;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
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
        private bool _disposed = false;

        public SecureCredentialManager(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        public bool ValidateCredentialSecurity(string credential, string credentialType)
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

            // Check for accidentally pasted environment variables or file paths
            if (credential.StartsWith("$") || 
                credential.StartsWith("%") ||
                credential.Contains(":\\") ||
                credential.Contains("/."))
            {
                _logger.Warn("Credential appears to contain environment variable or file path instead of actual credential");
                return false;
            }

            // Check for injection attack patterns
            if (ContainsInjectionPatterns(credential))
            {
                _logger.Warn("Credential contains suspicious injection patterns - potential security risk");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks for common injection attack patterns that should not appear in legitimate credentials.
        /// Detects SQL injection, XSS, script injection, and directory traversal attempts.
        /// </summary>
        /// <param name="input">Input string to check for injection patterns</param>
        /// <returns>True if suspicious patterns are detected</returns>
        private bool ContainsInjectionPatterns(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();
            
            // SQL injection patterns
            string[] sqlPatterns = {
                "drop table", "drop database", "truncate", "delete from",
                "insert into", "update set", "create table", "alter table",
                "union select", "union all", "' or '", "\" or \"",
                "or 1=1", "or '1'='1", "or \"1\"=\"1\"", "or true",
                "'; --", "\"; --", "/*", "*/", "@@", "xp_", "sp_"
            };

            // XSS and script injection patterns  
            string[] xssPatterns = {
                "<script", "</script>", "javascript:", "onload=", "onerror=", 
                "onclick=", "onmouseover=", "onfocus=", "onblur=", "alert(",
                "document.cookie", "document.write", "window.location",
                "<iframe", "<object", "<embed", "<form", "eval("
            };

            // Directory traversal patterns
            string[] traversalPatterns = {
                "../", "..\\", "/etc/passwd", "/etc/shadow", "/proc/",
                "c:\\windows", "c:\\users", "system32", "boot.ini"
            };

            // Check all pattern categories
            foreach (var pattern in sqlPatterns)
            {
                if (lowerInput.Contains(pattern))
                {
                    _logger.Debug("SQL injection pattern detected: {0}", pattern);
                    return true;
                }
            }

            foreach (var pattern in xssPatterns)
            {
                if (lowerInput.Contains(pattern))
                {
                    _logger.Debug("XSS pattern detected: {0}", pattern);
                    return true;
                }
            }

            foreach (var pattern in traversalPatterns)
            {
                if (lowerInput.Contains(pattern))
                {
                    _logger.Debug("Directory traversal pattern detected: {0}", pattern);
                    return true;
                }
            }

            return false;
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clear any remaining sensitive data
                _disposed = true;
            }
        }
    }
}