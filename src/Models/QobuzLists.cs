using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Wrapper class for lists of albums returned by the API.
    /// </summary>
    public class QobuzAlbumList
    {
        public List<QobuzAlbum> Items { get; set; } = new List<QobuzAlbum>();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Wrapper class for lists of artists returned by the API.
    /// </summary>
    public class QobuzArtistList
    {
        public List<QobuzArtist> Items { get; set; } = new List<QobuzArtist>();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Wrapper class for lists of tracks returned by the API.
    /// </summary>
    public class QobuzTrackList
    {
        public List<QobuzTrack> Items { get; set; } = new List<QobuzTrack>();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Wrapper class for lists of playlists returned by the API.
    /// </summary>
    public class QobuzPlaylistList
    {
        public List<QobuzPlaylist> Items { get; set; } = new List<QobuzPlaylist>();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Response for track search operations.
    /// </summary>
    public class QobuzTrackSearchResponse
    {
        public QobuzTrackList Tracks { get; set; } = new QobuzTrackList();
        public string Query { get; set; } = string.Empty;
        public int Total { get; set; }
    }
}
