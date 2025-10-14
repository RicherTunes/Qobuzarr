using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    public interface ISubstringMatcher
    {
        string NormalizeString(string input);
        double CalculateSimilarity(string s1, string s2);
        bool IsSimilar(string s1, string s2, double threshold);
        IEnumerable<TEntry> FindArtistMatches<TEntry>(IEnumerable<TEntry> entries, string searchArtist, string searchAlbum,
                                                     Func<TEntry, string> artistAccessor, Func<TEntry, string> albumAccessor,
                                                     double similarityThreshold) where TEntry : class;
        IEnumerable<TEntry> FindAlbumMatches<TEntry>(IEnumerable<TEntry> entries, string searchArtist, string searchAlbum,
                                                    Func<TEntry, string> artistAccessor, Func<TEntry, string> albumAccessor,
                                                    double similarityThreshold) where TEntry : class;
        IEnumerable<TEntry> FindFuzzyMatches<TEntry>(IEnumerable<TEntry> entries, string searchArtist, string searchAlbum,
                                                    Func<TEntry, string> artistAccessor, Func<TEntry, string> albumAccessor,
                                                    double similarityThreshold, int maxResults = 5) where TEntry : class;
    }
}

