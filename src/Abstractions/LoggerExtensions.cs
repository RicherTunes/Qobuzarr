using NLog;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    public static class LoggerExtensions
    {
        public static void InfoEvent(this IQobuzLogger logger, string eventId, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEvent(LogLevel.Info, eventId, message, args);
            }
            else
            {
                logger.Info("[" + eventId + "] " + message, args);
            }
        }

        public static void WarnEvent(this IQobuzLogger logger, string eventId, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEvent(LogLevel.Warn, eventId, message, args);
            }
            else
            {
                logger.Warn("[" + eventId + "] " + message, args);
            }
        }

        public static void InfoEvent(this IQobuzLogger logger, string eventId, System.Collections.Generic.IDictionary<string, object>? props, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEventProps(LogLevel.Info, eventId, props, message, args);
            }
            else
            {
                logger.Info("[" + eventId + "] " + AppendProps(props, message), args);
            }
        }

        public static void WarnEvent(this IQobuzLogger logger, string eventId, System.Collections.Generic.IDictionary<string, object>? props, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEventProps(LogLevel.Warn, eventId, props, message, args);
            }
            else
            {
                logger.Warn("[" + eventId + "] " + AppendProps(props, message), args);
            }
        }

        private static string AppendProps(System.Collections.Generic.IDictionary<string, object>? props, string message)
        {
            if (props == null || props.Count == 0) return message;
            try
            {
                var pairs = new System.Text.StringBuilder();
                foreach (var kv in props)
                {
                    if (pairs.Length > 0) pairs.Append(' ');
                    pairs.Append('[').Append(kv.Key).Append('=').Append(kv.Value).Append(']');
                }
                return pairs.ToString() + " " + message;
            }
            catch
            {
                return message;
            }
        }

        public static void DebugEvent(this IQobuzLogger logger, string eventId, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEvent(LogLevel.Debug, eventId, message, args);
            }
            else
            {
                logger.Debug("[" + eventId + "] " + message, args);
            }
        }

        public static void ErrorEvent(this IQobuzLogger logger, string eventId, string message, params object[] args)
        {
            if (logger is NLogAdapter nl)
            {
                nl.LogWithEvent(LogLevel.Error, eventId, message, args);
            }
            else
            {
                logger.Error("[" + eventId + "] " + message, args);
            }
        }
    }
}
