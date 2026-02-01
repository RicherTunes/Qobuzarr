namespace QobuzCLI.Services
{
    /// <summary>
    /// Interface for secure storage and retrieval of sensitive credentials.
    /// Provides encryption/decryption of sensitive data before storage.
    /// </summary>
    public interface ISecureCredentialStorage
    {
        /// <summary>
        /// Stores a credential securely using encryption.
        /// </summary>
        /// <param name="key">The key to identify the credential.</param>
        /// <param name="value">The sensitive value to store securely.</param>
        Task StoreCredentialAsync(string key, string value);

        /// <summary>
        /// Retrieves and decrypts a stored credential.
        /// </summary>
        /// <param name="key">The key that identifies the credential.</param>
        /// <returns>The decrypted credential value, or null if not found.</returns>
        Task<string?> RetrieveCredentialAsync(string key);

        /// <summary>
        /// Removes a stored credential.
        /// </summary>
        /// <param name="key">The key that identifies the credential to remove.</param>
        Task RemoveCredentialAsync(string key);

        /// <summary>
        /// Checks if a credential exists for the given key.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <returns>True if the credential exists, false otherwise.</returns>
        Task<bool> HasCredentialAsync(string key);
    }
}
