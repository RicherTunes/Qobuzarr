using System;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Provides album preview functionality before download.
    /// </summary>
    public interface IAlbumPreviewService
    {
        /// <summary>
        /// Displays a preview of an album and prompts for confirmation.
        /// </summary>
        /// <param name="album">The album to preview.</param>
        /// <param name="quality">The target download quality.</param>
        /// <returns>True if user confirms download; otherwise, false.</returns>
        Task<bool> PreviewAndConfirmAsync(QobuzAlbum album, QobuzAudioQuality quality);

        /// <summary>
        /// Displays album information without confirmation prompt.
        /// </summary>
        /// <param name="album">The album to display.</param>
        /// <param name="quality">The target download quality.</param>
        Task DisplayAlbumInfoAsync(QobuzAlbum album, QobuzAudioQuality quality);
    }

    /// <summary>
    /// Implementation of album preview service using Spectre.Console for rich output.
    /// </summary>
    public class AlbumPreviewService : IAlbumPreviewService
    {
        private readonly ILogger<AlbumPreviewService> _logger;

        public AlbumPreviewService(ILogger<AlbumPreviewService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> PreviewAndConfirmAsync(QobuzAlbum album, QobuzAudioQuality quality)
        {
            if (album == null)
            {
                _logger.LogWarning("Cannot preview null album");
                return false;
            }

            await DisplayAlbumInfoAsync(album, quality);

            // Add confirmation prompt
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Download this album at {GetQualityDescription(quality)} quality?[/]",
                defaultValue: true);

            return confirm;
        }

        public async Task DisplayAlbumInfoAsync(QobuzAlbum album, QobuzAudioQuality quality)
        {
            if (album == null)
            {
                _logger.LogWarning("Cannot display null album");
                return;
            }

            await Task.CompletedTask; // Async for future enhancements

            // Create album info panel
            var panel = new Panel(CreateAlbumContent(album, quality))
            {
                Header = new PanelHeader($"[bold cyan]{album.Title}[/]"),
                Border = BoxBorder.Rounded,
                Expand = true
            };

            AnsiConsole.Write(panel);

            // Display track listing if available
            if (album.TracksContainer?.Items?.Any() == true)
            {
                DisplayTrackListing(album);
            }

            // Display quality information
            DisplayQualityInfo(album, quality);
        }

        private string CreateAlbumContent(QobuzAlbum album, QobuzAudioQuality quality)
        {
            var content = new System.Text.StringBuilder();
            
            content.AppendLine($"[bold]Artist:[/] {album.Artist?.Name ?? "Unknown"}");
            content.AppendLine($"[bold]Album:[/] {album.Title ?? "Unknown"}");
            
            if (!string.IsNullOrEmpty(album.Version))
                content.AppendLine($"[bold]Version:[/] {album.Version}");
            
            if (album.ReleaseDateOriginal != null)
                content.AppendLine($"[bold]Release Date:[/] {album.ReleaseDateOriginal:yyyy-MM-dd}");
            
            content.AppendLine($"[bold]Label:[/] {album.Label?.Name ?? "Unknown"}");
            content.AppendLine($"[bold]Genre:[/] {album.Genre?.Name ?? "Unknown"}");
            
            if (album.TracksCount > 0)
                content.AppendLine($"[bold]Tracks:[/] {album.TracksCount}");
            
            if (album.DurationSeconds > 0)
            {
                var duration = TimeSpan.FromSeconds(album.DurationSeconds);
                content.AppendLine($"[bold]Duration:[/] {duration:hh\\:mm\\:ss}");
            }

            content.AppendLine($"[bold]Quality:[/] {GetQualityDescription(quality)}");
            
            if (album.MaximumSampleRate > 0 && album.MaximumBitDepth > 0)
            {
                content.AppendLine($"[bold]Max Available:[/] {album.MaximumBitDepth}bit/{album.MaximumSampleRate/1000.0:F1}kHz");
            }

            content.AppendLine($"[bold]Qobuz ID:[/] {album.Id}");

            return content.ToString();
        }

        private void DisplayTrackListing(QobuzAlbum album)
        {
            var table = new Table()
                .Title("[bold yellow]Track Listing[/]")
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("[bold]#[/]").Centered())
                .AddColumn(new TableColumn("[bold]Title[/]"))
                .AddColumn(new TableColumn("[bold]Duration[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]Quality[/]").Centered());

            foreach (var track in album.TracksContainer.Items.OrderBy(t => t.TrackNumber))
            {
                var duration = TimeSpan.FromSeconds(track.DurationSeconds);
                var quality = GetTrackQuality(track);
                
                table.AddRow(
                    track.TrackNumber.ToString(),
                    track.Title ?? "Unknown",
                    $"{duration:mm\\:ss}",
                    quality
                );
            }

            AnsiConsole.Write(table);
        }

        private void DisplayQualityInfo(QobuzAlbum album, QobuzAudioQuality targetQuality)
        {
            var qualityPanel = new Panel(CreateQualityContent(album, targetQuality))
            {
                Header = new PanelHeader("[bold green]Quality Information[/]"),
                Border = BoxBorder.Square
            };

            AnsiConsole.Write(qualityPanel);
        }

        private string CreateQualityContent(QobuzAlbum album, QobuzAudioQuality targetQuality)
        {
            var content = new System.Text.StringBuilder();
            
            content.AppendLine($"[bold]Requested Quality:[/] {GetQualityDescription(targetQuality)}");
            
            // Check if requested quality is available
            var maxAvailable = GetMaxAvailableQuality(album);
            content.AppendLine($"[bold]Maximum Available:[/] {GetQualityDescription(maxAvailable)}");
            
            if ((int)targetQuality > (int)maxAvailable)
            {
                content.AppendLine("[yellow]⚠ Requested quality exceeds available quality[/]");
                content.AppendLine($"[yellow]Will download at: {GetQualityDescription(maxAvailable)}[/]");
            }
            else
            {
                content.AppendLine("[green]✓ Requested quality is available[/]");
            }

            // Estimate download size
            var estimatedSize = EstimateDownloadSize(album, Math.Min((int)targetQuality, (int)maxAvailable));
            if (estimatedSize > 0)
            {
                content.AppendLine($"[bold]Estimated Size:[/] {FormatFileSize(estimatedSize)}");
            }

            return content.ToString();
        }

        private string GetTrackQuality(QobuzTrack track)
        {
            if (track.MaximumSampleRate > 48000 || track.MaximumBitDepth > 16)
                return $"{track.MaximumBitDepth}bit/{track.MaximumSampleRate/1000.0:F1}kHz";
            if (track.MaximumBitDepth == 16 && track.MaximumSampleRate == 44100)
                return "CD Quality";
            return "Lossy";
        }

        private QobuzAudioQuality GetMaxAvailableQuality(QobuzAlbum album)
        {
            if (album.MaximumSampleRate >= 192000)
                return QobuzAudioQuality.FLACHiRes24Bit192Khz;
            if (album.MaximumSampleRate >= 96000)
                return QobuzAudioQuality.FLACHiRes24Bit96kHz;
            if (album.MaximumBitDepth >= 16)
                return QobuzAudioQuality.FLACLossless;
            return QobuzAudioQuality.MP3320;
        }

        private string GetQualityDescription(QobuzAudioQuality quality)
        {
            return quality switch
            {
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "Hi-Res 24-bit/192kHz",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "Hi-Res 24-bit/96kHz",
                QobuzAudioQuality.FLACLossless => "CD Quality (16-bit/44.1kHz)",
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                _ => quality.ToString()
            };
        }

        private long EstimateDownloadSize(QobuzAlbum album, int qualityLevel)
        {
            if (album.Duration <= TimeSpan.Zero)
                return 0;

            // Rough estimates based on quality (bytes per second)
            var bytesPerSecond = qualityLevel switch
            {
                >= 7 => 576000,  // Hi-Res 192kHz
                >= 6 => 288000,  // Hi-Res 96kHz
                >= 5 => 144000,  // Hi-Res 48kHz
                >= 4 => 176400,  // CD Quality
                >= 3 => 40000,   // MP3 320
                _ => 18750       // MP3 150
            };

            return (long)(album.Duration.TotalSeconds * bytesPerSecond);
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}