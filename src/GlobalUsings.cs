// Global usings to leverage shared utilities from Lidarr.Plugin.Common across the plugin
// Map Guard to the shared implementation for concise usage across the codebase
global using Guard = Lidarr.Plugin.Common.Utilities.Guard;
// Alias the local StringSimilarity to avoid ambiguity with other utilities and make intent explicit
global using CommonStringSimilarity = Lidarr.Plugin.Qobuzarr.Utilities.StringSimilarity;
