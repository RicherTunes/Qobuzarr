using System;
using System.IO;
using FluentAssertions;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Xunit;

namespace Qobuzarr.Tests.Unit.Authentication
{
    /// <summary>
    /// Verifies that <see cref="SessionManager.GetDefaultSessionFilePath"/> always
    /// returns a rooted (absolute) path regardless of the host environment.
    /// Regression test for the Docker empty-HOME bug where the old hand-rolled
    /// fallback produced a relative path resolving to /app/bin/ArrPlugins/… →
    /// UnauthorizedAccessException.
    /// </summary>
    public class SessionManagerPathTests
    {
        [Fact]
        public void GetDefaultSessionFilePath_ReturnsRootedPath()
        {
            // Act
            var path = SessionManager.GetDefaultSessionFilePath();

            // Assert — must always be absolute so the runtime doesn't resolve it
            // against /app/bin/ (Lidarr's process cwd in Docker).
            Path.IsPathRooted(path).Should().BeTrue(
                "session file path must be absolute to prevent resolution against /app/bin/");
        }

        [Fact]
        public void GetDefaultSessionFilePath_EndsWithSessionFileName()
        {
            // Act
            var path = SessionManager.GetDefaultSessionFilePath();

            // Assert — the filename constant must be preserved.
            Path.GetFileName(path).Should().Be(SessionManager.DefaultSessionFileName);
        }

        [Fact]
        public void GetDefaultSessionFilePath_ContainsStorageFolderName()
        {
            // Act
            var path = SessionManager.GetDefaultSessionFilePath();

            // Assert — the Qobuzarr leaf directory (DefaultStorageFolder) must appear
            // somewhere in the path so sessions are not stored in a shared root.
            path.Should().Contain(SessionManager.DefaultStorageFolder,
                "session files must be scoped under the Qobuzarr sub-directory");
        }

        /// <summary>
        /// Simulates a Docker container where both ApplicationData and UserProfile
        /// are empty strings (no HOME set). The fix delegates to PluginConfigRoots
        /// which falls back to /config/Qobuzarr or the LIDARR_PLUGIN_CONFIG override,
        /// both of which are rooted.
        /// </summary>
        [Fact]
        public void PluginConfigRoots_WithOverride_ReturnsRootedPath()
        {
            // Arrange — use a known absolute override path.
            var overrideDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            var stubEnv = new StubConfigEnvironment(
                envVars: new System.Collections.Generic.Dictionary<string, string?>
                {
                    [PluginConfigRoots.OverrideEnvVar] = overrideDir
                },
                appData: string.Empty,
                xdgConfigHome: null,
                home: null,
                dockerExists: false);

            // Act
            var resolved = PluginConfigRoots.Resolve("Qobuzarr", stubEnv);

            // Assert
            Path.IsPathRooted(resolved).Should().BeTrue();
            resolved.Should().Contain("Qobuzarr");
        }

        /// <summary>
        /// Simulates a Docker container with empty HOME and no AppData:
        /// PluginConfigRoots falls back to /config (Docker convention).
        /// </summary>
        [Fact]
        public void PluginConfigRoots_EmptyEnvironment_DockerFallback_ReturnsRootedPath()
        {
            // Arrange
            var stubEnv = new StubConfigEnvironment(
                envVars: new System.Collections.Generic.Dictionary<string, string?>(),
                appData: string.Empty,
                xdgConfigHome: null,
                home: null,
                // Simulate /config existing (Docker hotio convention)
                dockerExists: true);

            // Act
            var resolved = PluginConfigRoots.Resolve("Qobuzarr", stubEnv);

            // Assert
            Path.IsPathRooted(resolved).Should().BeTrue();
            resolved.Should().StartWith("/config");
        }

        // ---------------------------------------------------------------------------
        // Test double for IConfigEnvironment
        // ---------------------------------------------------------------------------

        private sealed class StubConfigEnvironment : IConfigEnvironment
        {
            private readonly System.Collections.Generic.Dictionary<string, string?> _envVars;
            private readonly string _appData;
            private readonly string? _xdgConfigHome;
            private readonly string? _home;
            private readonly bool _dockerExists;

            public StubConfigEnvironment(
                System.Collections.Generic.Dictionary<string, string?> envVars,
                string appData,
                string? xdgConfigHome,
                string? home,
                bool dockerExists)
            {
                _envVars = envVars;
                _appData = appData;
                _xdgConfigHome = xdgConfigHome;
                _home = home;
                _dockerExists = dockerExists;
            }

            public string? GetEnvironmentVariable(string name)
            {
                if (_envVars.TryGetValue(name, out var val)) return val;
                if (name == "XDG_CONFIG_HOME") return _xdgConfigHome;
                if (name == "HOME") return _home;
                return null;
            }

            public string GetFolderPath(Environment.SpecialFolder folder)
                => folder == Environment.SpecialFolder.ApplicationData ? _appData : string.Empty;

            public bool DirectoryExists(string path)
                => _dockerExists && path == PluginConfigRoots.DefaultDockerConfigRoot;
        }
    }
}
