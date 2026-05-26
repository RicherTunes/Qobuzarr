using System.Collections.Generic;
using NLog;
using Lidarr.Plugin.Common.TestKit.Helpers;

namespace Qobuzarr.Tests.Helpers
{
    /// <summary>
    /// Thin shim preserving the original <c>Qobuzarr.Tests.Helpers.TestLogger</c> API.
    /// All implementation is delegated to <see cref="NLogTestLogger"/> in
    /// <c>Lidarr.Plugin.Common.TestKit</c>.
    /// </summary>
    public static class TestLogger
    {
        /// <inheritdoc cref="NLogTestLogger.Create"/>
        public static Logger Create(string name = "TestLogger")
            => NLogTestLogger.Create(name);

        /// <inheritdoc cref="NLogTestLogger.GetLoggedMessages"/>
        public static IList<string> GetLoggedMessages()
            => NLogTestLogger.GetLoggedMessages();

        /// <inheritdoc cref="NLogTestLogger.ClearLoggedMessages"/>
        public static void ClearLoggedMessages()
            => NLogTestLogger.ClearLoggedMessages();
    }
}
