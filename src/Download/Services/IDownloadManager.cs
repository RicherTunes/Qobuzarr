using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Download.Clients;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Manages the download lifecycle for albums and tracks
    /// </summary>
    public interface IDownloadManager
    {
        /// <summary>
        /// Initiates a download for a remote album
        /// </summary>
        Task<string> DownloadAlbumAsync(RemoteAlbum remoteAlbum, IIndexer indexer);
        
        /// <summary>
        /// Downloads all tracks in an album
        /// </summary>
        Task DownloadAlbumTracksAsync(QobuzDownloadItem downloadItem, QobuzAlbum album);
        
        /// <summary>
        /// Downloads a single track
        /// </summary>
        Task DownloadSingleTrackAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzTrack track);
        
        /// <summary>
        /// Gets album details from Qobuz API
        /// </summary>
        Task<QobuzAlbum> GetAlbumDetailsAsync(string albumId);
        
        /// <summary>
        /// Extracts album ID from release info
        /// </summary>
        string ExtractAlbumIdFromRelease(ReleaseInfo release);
    }
}