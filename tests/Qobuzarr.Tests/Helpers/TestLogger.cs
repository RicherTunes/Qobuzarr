using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Qobuzarr.Tests.Helpers
{
    /// <summary>
    /// Test helper that provides a real NLog Logger instance configured for testing.
    /// Captures log output to an in-memory target for assertion verification.
    /// </summary>
    public static class TestLogger
    {
        private static Logger _testLogger;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets a real NLog Logger instance configured with a MemoryTarget for test verification.
        /// </summary>
        public static Logger Create(string name = "TestLogger")
        {
            lock (_lock)
            {
                if (_testLogger == null)
                {
                    var config = new LoggingConfiguration();

                    var memoryTarget = new MemoryTarget("testMemory")
                    {
                        Layout = "${level:uppercase=true}: ${message} ${exception:format=tostring}"
                    };

                    config.AddTarget(memoryTarget);
                    config.AddRuleForAllLevels(memoryTarget);

                    LogManager.Configuration = config;
                    _testLogger = LogManager.GetLogger(name);
                }

                return _testLogger;
            }
        }

        /// <summary>
        /// Gets logged messages from the memory target for test assertions.
        /// Only works with loggers created via Create() method.
        /// </summary>
        public static IList<string> GetLoggedMessages()
        {
            var memoryTarget = LogManager.Configuration?.FindTargetByName<MemoryTarget>("testMemory");
            return memoryTarget?.Logs ?? new List<string>();
        }

        /// <summary>
        /// Clears all logged messages from the memory target.
        /// </summary>
        public static void ClearLoggedMessages()
        {
            var memoryTarget = LogManager.Configuration?.FindTargetByName<MemoryTarget>("testMemory");
            memoryTarget?.Logs.Clear();
        }
    }
}
