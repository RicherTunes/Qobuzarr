using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    // Thin shim to maintain existing call sites while delegating to shared Guard
    public static class Guard
    {
        public static T NotNull<T>(T value, string? paramName = null) where T : class
            => Lidarr.Plugin.Common.Utilities.Guard.NotNull(value, paramName);

        public static string NotNullOrEmpty(string value, string? paramName = null)
            => Lidarr.Plugin.Common.Utilities.Guard.NotNullOrEmpty(value, paramName);

        public static string NotNullOrWhiteSpace(string value, string? paramName = null)
            => Lidarr.Plugin.Common.Utilities.Guard.NotNullOrWhiteSpace(value, paramName);

        public static IEnumerable<T> NotNullOrEmpty<T>(IEnumerable<T> value, string? paramName = null)
            => Lidarr.Plugin.Common.Utilities.Guard.NotNullOrEmpty(value, paramName);

        public static T InRange<T>(T value, T min, T max, string? paramName = null) where T : IComparable<T>
            => Lidarr.Plugin.Common.Utilities.Guard.InRange(value, min, max, paramName);

        public static T GreaterThan<T>(T value, T min, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(min) <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, $"Value must be greater than {min}.");
            }
            return value;
        }

        public static T GreaterThanOrEqualTo<T>(T value, T min, string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, $"Value must be greater than or equal to {min}.");
            }
            return value;
        }

        // Convenience numeric overloads to aid type inference at call sites
        public static int GreaterThan(int value, int min, string? paramName = null)
            => GreaterThan<int>(value, min, paramName);

        public static int GreaterThanOrEqualTo(int value, int min, string? paramName = null)
            => GreaterThanOrEqualTo<int>(value, min, paramName);

        public static double GreaterThan(double value, double min, string? paramName = null)
            => GreaterThan<double>(value, min, paramName);

        public static double GreaterThanOrEqualTo(double value, double min, string? paramName = null)
            => GreaterThanOrEqualTo<double>(value, min, paramName);

        public static string ValidFilePath(string path, string? paramName = null)
            => Lidarr.Plugin.Common.Utilities.Guard.ValidFilePath(path, paramName);
    }
}
