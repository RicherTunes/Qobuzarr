using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Interface for secure configuration management that encrypts sensitive credentials.
    /// </summary>
    public interface ISecureConfigService
    {
        /// <summary>
        /// Loads configuration with secure credential retrieval.
        /// </summary>
        Task<QobuzConfig> LoadConfigAsync();

        /// <summary>
        /// Saves configuration with secure credential storage.
        /// </summary>
        Task SaveConfigAsync(QobuzConfig config);

        /// <summary>
        /// Updates a specific credential securely.
        /// </summary>
        /// <param name="credentialType">Type of credential (password, authtoken, appsecret).</param>
        /// <param name="value">The credential value to store securely.</param>
        Task UpdateCredentialAsync(string credentialType, string value);

        /// <summary>
        /// Checks if secure credentials are available.
        /// </summary>
        Task<bool> HasSecureCredentialsAsync();

        /// <summary>
        /// Migrates existing plain-text credentials to secure storage.
        /// </summary>
        Task MigrateToSecureStorageAsync();
    }
}
