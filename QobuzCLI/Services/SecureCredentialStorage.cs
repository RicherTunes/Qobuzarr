using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Provides secure storage for sensitive credentials using Windows DPAPI or cross-platform alternatives.
    /// Encrypts sensitive data before storage and decrypts it when retrieved.
    /// </summary>
    public class SecureCredentialStorage : ISecureCredentialStorage
    {
        private readonly ILogger<SecureCredentialStorage> _logger;
        private readonly string _credentialStorePath;

        public SecureCredentialStorage(ILogger<SecureCredentialStorage> logger)
        {
            _logger = logger;
            
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var qobuzDirectory = Path.Combine(homeDirectory, ".qobuz");
            
            if (!Directory.Exists(qobuzDirectory))
            {
                Directory.CreateDirectory(qobuzDirectory);
            }
            
            _credentialStorePath = Path.Combine(qobuzDirectory, "credentials.dat");
        }

        /// <summary>
        /// Stores a credential securely using platform-appropriate encryption
        /// </summary>
        public async Task StoreCredentialAsync(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            try
            {
                var encryptedData = EncryptString(value);
                var credentials = await LoadCredentialsAsync();
                credentials[key] = encryptedData;
                await SaveCredentialsAsync(credentials);
                
                _logger.LogDebug("Credential stored securely for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store credential for key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// Retrieves a credential and decrypts it
        /// </summary>
        public async Task<string?> RetrieveCredentialAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            try
            {
                var credentials = await LoadCredentialsAsync();
                if (!credentials.TryGetValue(key, out var encryptedData))
                    return null;

                var decryptedValue = DecryptString(encryptedData);
                _logger.LogDebug("Credential retrieved for key: {Key}", key);
                return decryptedValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve credential for key: {Key}", key);
                return null;
            }
        }

        /// <summary>
        /// Removes a stored credential
        /// </summary>
        public async Task RemoveCredentialAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            try
            {
                var credentials = await LoadCredentialsAsync();
                if (credentials.Remove(key))
                {
                    await SaveCredentialsAsync(credentials);
                    _logger.LogDebug("Credential removed for key: {Key}", key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove credential for key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// Checks if a credential exists for the given key
        /// </summary>
        public async Task<bool> HasCredentialAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            try
            {
                var credentials = await LoadCredentialsAsync();
                return credentials.ContainsKey(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check credential existence for key: {Key}", key);
                return false;
            }
        }

        private async Task<Dictionary<string, string>> LoadCredentialsAsync()
        {
            if (!File.Exists(_credentialStorePath))
                return new Dictionary<string, string>();

            try
            {
                var encryptedContent = await File.ReadAllBytesAsync(_credentialStorePath);
                if (encryptedContent.Length == 0)
                    return new Dictionary<string, string>();

                var json = DecryptString(Convert.ToBase64String(encryptedContent));
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                // If decryption fails, return empty dictionary (fresh start)
                return new Dictionary<string, string>();
            }
        }

        private async Task SaveCredentialsAsync(Dictionary<string, string> credentials)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(credentials);
            var encryptedContent = EncryptString(json);
            var encryptedBytes = Convert.FromBase64String(encryptedContent);
            await File.WriteAllBytesAsync(_credentialStorePath, encryptedBytes);
        }

        private string EncryptString(string plainText)
        {
            try
            {
                // Use DPAPI on Windows for enhanced security
                if (OperatingSystem.IsWindows())
                {
#if WINDOWS
                    var data = Encoding.UTF8.GetBytes(plainText);
                    var encryptedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                    return Convert.ToBase64String(encryptedData);
#else
                    throw new NotSupportedException("Windows DPAPI not available on this platform");
#endif
                }
                else
                {
                    // Use AES encryption for cross-platform compatibility
                    return EncryptStringAes(plainText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt string");
                throw;
            }
        }

        private string DecryptString(string encryptedText)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
#if WINDOWS
                    var encryptedData = Convert.FromBase64String(encryptedText);
                    var data = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(data);
#else
                    throw new NotSupportedException("Windows DPAPI not available on this platform");
#endif
                }
                else
                {
                    // Use AES decryption for cross-platform compatibility
                    return DecryptStringAes(encryptedText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt string");
                throw;
            }
        }

        /// <summary>
        /// Encrypts a string using AES encryption for cross-platform compatibility
        /// </summary>
        private string EncryptStringAes(string plainText)
        {
            var key = GetOrCreateAesKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine IV and encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// Decrypts a string using AES decryption for cross-platform compatibility
        /// </summary>
        private string DecryptStringAes(string encryptedText)
        {
            var key = GetOrCreateAesKey();
            var data = Convert.FromBase64String(encryptedText);

            using var aes = Aes.Create();
            aes.Key = key;

            // Extract IV from the beginning of the data
            var iv = new byte[16]; // AES block size is 16 bytes
            Array.Copy(data, 0, iv, 0, iv.Length);
            aes.IV = iv;

            // Extract encrypted data
            var encryptedBytes = new byte[data.Length - iv.Length];
            Array.Copy(data, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        /// <summary>
        /// Gets or creates a user-specific AES key for encryption
        /// </summary>
        private byte[] GetOrCreateAesKey()
        {
            var credentialsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qobuz", "credentials");
            Directory.CreateDirectory(credentialsDir);
            var keyPath = Path.Combine(credentialsDir, ".aeskey");
            
            if (File.Exists(keyPath))
            {
                try
                {
                    return File.ReadAllBytes(keyPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read existing AES key, generating new one");
                }
            }

            // Generate new key
            using var aes = Aes.Create();
            aes.GenerateKey();
            var key = aes.Key;

            try
            {
                // Store the key securely
                File.WriteAllBytes(keyPath, key);
                
                // Set file permissions on Unix-like systems (best effort)
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        // Use chmod command as fallback for .NET 6.0 compatibility
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"600 \"{keyPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                    }
                }
                catch
                {
                    // File permission setting failed, but key is still created
                    _logger.LogWarning("Could not set secure file permissions on AES key file");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store AES key");
                throw;
            }

            return key;
        }
    }
}