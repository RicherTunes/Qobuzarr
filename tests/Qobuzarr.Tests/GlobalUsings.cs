// Local test shims to avoid tight coupling to ext/Lidarr.Plugin.Common commit
global using TestPreviewDetectionUtility = Qobuzarr.Tests.Utilities.PreviewDetectionUtility;
global using StringSimilarity = Qobuzarr.Tests.Utilities.StringSimilarity;
// Note: avoid importing Common ML namespace globally to prevent type name collisions
