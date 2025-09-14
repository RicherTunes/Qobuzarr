// Global usings to leverage shared utilities from Lidarr.Plugin.Common across the plugin
// Map Guard to the shared implementation for concise usage across the codebase
global using Guard = Lidarr.Plugin.Common.Utilities.Guard;

// Ensure references like "Utilities.StringSimilarity" resolve to the plugin's local utilities
// to avoid ambiguity with similarly named types in Lidarr.Plugin.Common
global using Utilities = Lidarr.Plugin.Qobuzarr.Utilities;
