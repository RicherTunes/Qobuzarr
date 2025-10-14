// Minimal compatibility stubs for CI and external PRs when ext/Lidarr.Plugin.Common is not available.
// These stubs cover only the APIs consumed by this repository and are NOT feature-complete.
#if COMMON_STUBS
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Common.Utilities
{
    public static class Guard
    {
        public static T NotNull<T>(T value, string? name = null) where T : class
            => value ?? throw new ArgumentNullException(name ?? nameof(value));

        public static string NotNullOrWhiteSpace(string value, string? name = null)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be null or whitespace", name ?? nameof(value)) : value;

        public static int InRange(int value, int min, int max, string? name = null)
            => (value < min || value > max) ? throw new ArgumentOutOfRangeException(name ?? nameof(value)) : value;

        public static double InRange(double value, double min, double max, string? name = null)
            => (value < min || value > max) ? throw new ArgumentOutOfRangeException(name ?? nameof(value)) : value;
    }

    // Basic placeholder; production similarity comes from the real shared lib.
    public static class StringSimilarity
    {
        public static double Calculate(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 1.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
            var min = Math.Min(a.Length, b.Length);
            var match = 0;
            for (int i = 0; i < min; i++) if (char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) match++;
            return (double)match / Math.Max(a.Length, b.Length);
        }
    }

    public static class RetryUtilities { }
}

namespace Lidarr.Plugin.Common.Services.Globalization
{
    // Very small shim; real implementation is Unicode-aware and far more robust.
    public sealed class UnicodeNormalizer
    {
        public UnicodeNormalizer(object? _logger) { }
        public double CalculateInternationalSimilarity(string a, string b)
            => Lidarr.Plugin.Common.Utilities.StringSimilarity.Calculate(a, b);
    }
}

namespace Lidarr.Plugin.Common.Services.Performance
{
    public interface IUniversalAdaptiveRateLimiter
    {
        Task WaitIfNeededAsync(string serviceName, string endpoint, CancellationToken ct);
        void RecordResponse(string serviceName, string endpoint, object response);
    }
}

namespace Lidarr.Plugin.Common.Security
{
    public static class Sanitize
    {
        public static string PathSegment(string value) => value ?? string.Empty;
    }
}
#endif

