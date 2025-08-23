using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Wrapper for securely handling sensitive credentials in memory using SecureString.
    /// Minimizes the window of exposure for passwords and secrets in memory dumps.
    /// </summary>
    public sealed class SecureCredentialWrapper : IDisposable
    {
        private SecureString? _secureValue;
        private bool _disposed;

        /// <summary>
        /// Creates a secure credential wrapper from a regular string.
        /// The original string should be cleared immediately after creating this wrapper.
        /// </summary>
        /// <param name="credential">The credential to secure</param>
        public SecureCredentialWrapper(string credential)
        {
            if (string.IsNullOrEmpty(credential))
            {
                _secureValue = new SecureString();
            }
            else
            {
                _secureValue = new SecureString();
                foreach (char c in credential)
                {
                    _secureValue.AppendChar(c);
                }
                _secureValue.MakeReadOnly();
            }
        }

        /// <summary>
        /// Creates an empty secure credential wrapper.
        /// </summary>
        public SecureCredentialWrapper()
        {
            _secureValue = new SecureString();
        }

        /// <summary>
        /// Gets the length of the stored credential.
        /// </summary>
        public int Length
        {
            get
            {
                ThrowIfDisposed();
                return _secureValue?.Length ?? 0;
            }
        }

        /// <summary>
        /// Returns true if the credential is empty or null.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                ThrowIfDisposed();
                return _secureValue == null || _secureValue.Length == 0;
            }
        }

        /// <summary>
        /// Safely converts the SecureString to a regular string for API usage.
        /// The returned string should be used immediately and then cleared.
        /// </summary>
        /// <returns>The credential as a regular string</returns>
        public string ToUnsecureString()
        {
            ThrowIfDisposed();
            
            if (_secureValue == null || _secureValue.Length == 0)
                return string.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(_secureValue);
                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
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
        /// Executes an action with the unsecured credential string and immediately clears it.
        /// This is the preferred way to use credentials for API calls.
        /// </summary>
        /// <param name="action">Action to execute with the credential</param>
        public void UseCredential(Action<string> action)
        {
            ThrowIfDisposed();
            
            var unsecuredCredential = ToUnsecureString();
            try
            {
                action(unsecuredCredential);
            }
            finally
            {
                // Clear the string from memory (best effort)
                ClearStringFromMemory(unsecuredCredential);
            }
        }

        /// <summary>
        /// Executes a function with the unsecured credential string and immediately clears it.
        /// Returns the result of the function.
        /// </summary>
        /// <typeparam name="T">Return type of the function</typeparam>
        /// <param name="func">Function to execute with the credential</param>
        /// <returns>Result of the function</returns>
        public T UseCredential<T>(Func<string, T> func)
        {
            ThrowIfDisposed();
            
            var unsecuredCredential = ToUnsecureString();
            try
            {
                return func(unsecuredCredential);
            }
            finally
            {
                // Clear the string from memory (best effort)
                ClearStringFromMemory(unsecuredCredential);
            }
        }

        /// <summary>
        /// Attempts to clear a string from memory by zeroing its characters.
        /// This is best-effort due to .NET string immutability limitations.
        /// </summary>
        /// <param name="value">String to clear from memory</param>
        private static void ClearStringFromMemory(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            try
            {
                unsafe
                {
                    fixed (char* ptr = value)
                    {
                        for (int i = 0; i < value.Length; i++)
                        {
                            ptr[i] = '\0';
                        }
                    }
                }
            }
            catch
            {
                // Best effort - if clearing fails, at least the SecureString is protected
            }
        }

        /// <summary>
        /// Executes an async function with the unsecured credential string and immediately clears it.
        /// This is the preferred way to use credentials for async API calls.
        /// </summary>
        /// <typeparam name="T">Return type of the function</typeparam>
        /// <param name="func">Function to execute with the credential</param>
        /// <returns>Result of the function</returns>
        public async Task<T> UseCredentialAsync<T>(Func<string, Task<T>> func)
        {
            ThrowIfDisposed();
            
            var unsecuredCredential = ToUnsecureString();
            try
            {
                return await func(unsecuredCredential).ConfigureAwait(false);
            }
            finally
            {
                // Clear the string from memory (best effort)
                ClearStringFromMemory(unsecuredCredential);
            }
        }

        /// <summary>
        /// Clears the credential from memory.
        /// </summary>
        public void Clear()
        {
            if (_secureValue != null && !_disposed)
            {
                _secureValue.Clear();
            }
        }

        /// <summary>
        /// Updates the credential value.
        /// </summary>
        /// <param name="newCredential">New credential value</param>
        public void UpdateCredential(string newCredential)
        {
            ThrowIfDisposed();
            
            _secureValue?.Clear();
            _secureValue?.Dispose();
            
            _secureValue = new SecureString();
            if (!string.IsNullOrEmpty(newCredential))
            {
                foreach (char c in newCredential)
                {
                    _secureValue.AppendChar(c);
                }
            }
            _secureValue.MakeReadOnly();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecureCredentialWrapper));
        }

        /// <summary>
        /// Disposes the secure credential wrapper and clears memory.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _secureValue?.Clear();
                _secureValue?.Dispose();
                _secureValue = null;
                _disposed = true;
            }
        }
    }
}