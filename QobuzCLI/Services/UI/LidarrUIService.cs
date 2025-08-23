using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace QobuzCLI.Services.UI
{
    /// <summary>
    /// Implementation of the Lidarr UI service.
    /// Centralizes all display logic for consistent UI rendering.
    /// </summary>
    public class LidarrUIService : ILidarrUIService
    {
        public void DisplayAlbumSummary(IEnumerable<LidarrAlbum> albums, int maxRows = 10)
        {
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Artist");
            table.AddColumn("Album");
            table.AddColumn("Year");
            table.AddColumn("Tracks");
            table.AddColumn("Type");

            var albumList = albums.Take(maxRows).ToList();
            foreach (var album in albumList)
            {
                table.AddRow(
                    album.Artist?.ArtistName ?? "Unknown",
                    album.Title ?? "Unknown",
                    album.ReleaseDate?.Year.ToString() ?? "Unknown",
                    (album.Statistics?.TrackFileCount ?? 0).ToString(),
                    album.AlbumType ?? "Unknown"
                );
            }

            var totalCount = albums.Count();
            if (totalCount > maxRows)
            {
                table.AddRow("[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]");
                table.AddRow($"[dim]+{totalCount - maxRows} more albums[/]", "", "", "", "");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        public void DisplayValidationSummary(IList<AlbumDownloadItem> validatedItems, int maxRows = 10)
        {
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Artist");
            table.AddColumn("Album");
            table.AddColumn("Qobuz Quality");
            table.AddColumn("Warnings");

            var items = validatedItems.Take(maxRows).ToList();
            foreach (var item in items)
            {
                var qobuzAlbum = item.QobuzAlbum;
                var warnings = item.ValidationMessages?.Any() == true 
                    ? string.Join("; ", item.ValidationMessages)
                    : "None";

                table.AddRow(
                    item.LidarrAlbum.Artist?.ArtistName ?? "Unknown",
                    item.LidarrAlbum.Title ?? "Unknown",
                    GetQualityString(qobuzAlbum),
                    warnings
                );
            }

            if (validatedItems.Count > maxRows)
            {
                table.AddRow("[dim]...[/]", "[dim]...[/]", "[dim]...[/]", "[dim]...[/]");
                table.AddRow($"[dim]+{validatedItems.Count - maxRows} more albums[/]", "", "", "");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        public void DisplayQualityProfileSummary(IList<AlbumDownloadItem> validatedItems)
        {
            AnsiConsole.MarkupLine("\n[cyan]📊 Quality Profile Summary:[/]");

            // Group by quality profile
            var profileGroups = validatedItems
                .GroupBy(item => item.QualityProfile?.Name ?? "Default")
                .OrderBy(g => g.Key);

            var table = new Table();
            table.AddColumn("Quality Profile");
            table.AddColumn("Albums");
            table.AddColumn("Selected Quality");
            table.AddColumn("Sample Albums");

            foreach (var group in profileGroups)
            {
                var sampleAlbums = group.Take(2).Select(item => 
                    $"{item.LidarrAlbum.Artist?.ArtistName} - {item.LidarrAlbum.Title}");
                var selectedQualities = group.Select(item => item.SelectedQobuzQuality).Distinct();

                table.AddRow(
                    group.Key,
                    group.Count().ToString(),
                    string.Join(", ", selectedQualities),
                    string.Join("; ", sampleAlbums) + (group.Count() > 2 ? "..." : "")
                );
            }

            AnsiConsole.Write(table);

            // Show quality distribution
            var qualityDistribution = validatedItems
                .GroupBy(item => item.SelectedQobuzQuality ?? "Unknown")
                .OrderByDescending(g => g.Count());

            AnsiConsole.MarkupLine("\n[cyan]📈 Quality Distribution:[/]");
            foreach (var qualityGroup in qualityDistribution)
            {
                var percentage = (double)qualityGroup.Count() / validatedItems.Count * 100;
                AnsiConsole.MarkupLine($"[dim]• {qualityGroup.Key}: {qualityGroup.Count()} albums ({percentage:F1}%)[/]");
            }

            AnsiConsole.WriteLine();
        }

        public void DisplayDryRunResults(IList<AlbumDownloadItem> validatedItems, bool immediate, string quality)
        {
            AnsiConsole.MarkupLine("[yellow]🔍 DRY RUN RESULTS[/]");
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"[green]✅ Operation: {(immediate ? "Immediate Download" : "Add to Queue")}[/]");
            AnsiConsole.MarkupLine($"[green]✅ Quality: {quality ?? "flac-max"}[/]");
            AnsiConsole.MarkupLine($"[green]✅ Albums to process: {validatedItems.Count}[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Artist");
            table.AddColumn("Album");
            table.AddColumn("Tracks");
            table.AddColumn("Qobuz Quality");

            foreach (var item in validatedItems)
            {
                table.AddRow(
                    item.LidarrAlbum.Artist?.ArtistName ?? "Unknown",
                    item.LidarrAlbum.Title ?? "Unknown",
                    item.QobuzAlbum.TracksCount.ToString(),
                    GetQualityString(item.QobuzAlbum)
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Use --dry-run=false to proceed with the operation[/]");
        }

        public void DisplayExportSummary(List<LidarrAlbum> albums, string format, bool verbose)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📊 Export Summary:[/]");

            // Summary by album type
            var typeGroups = albums.GroupBy(a => a.AlbumType?.ToLowerInvariant() ?? "unknown")
                                   .OrderByDescending(g => g.Count());

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Album Type");
            table.AddColumn("Count");

            foreach (var group in typeGroups)
            {
                table.AddRow(group.Key, group.Count().ToString());
            }

            AnsiConsole.Write(table);

            if (verbose)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[cyan]📅 Release Year Distribution:[/]");
                
                var yearGroups = albums.Where(a => a.ReleaseDate.HasValue)
                                      .GroupBy(a => a.ReleaseDate!.Value.Year / 10 * 10) // Group by decade
                                      .OrderBy(g => g.Key);

                foreach (var group in yearGroups.Take(5))
                {
                    var decade = $"{group.Key}s";
                    AnsiConsole.MarkupLine($"[dim]• {decade}: {group.Count()} albums[/]");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]💡 Usage with Qobuz CLI:[/]");
            
            switch (format.ToLower())
            {
                case "json":
                    AnsiConsole.MarkupLine("[dim]qobuz download --from-file exported-albums.json[/]");
                    break;
                case "txt":
                    AnsiConsole.MarkupLine("[dim]Use the text file to manually copy search queries[/]");
                    break;
                case "csv":
                    AnsiConsole.MarkupLine("[dim]Import into spreadsheet for analysis and filtering[/]");
                    break;
            }
        }

        public void ShowProgress(string message, int current, int total)
        {
            var percentage = total > 0 ? (current * 100.0 / total) : 0;
            AnsiConsole.MarkupLine($"[cyan]{message}[/] [{current}/{total}] ({percentage:F1}%)");
        }

        public void ShowError(string message, string details = null, bool verbose = false)
        {
            AnsiConsole.MarkupLine($"[red]❌ {message}[/]");
            
            if (!string.IsNullOrEmpty(details))
            {
                AnsiConsole.MarkupLine($"[dim]{details}[/]");
            }
            
            if (verbose && !string.IsNullOrEmpty(details))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Full error details:[/]");
                AnsiConsole.WriteLine(details);
            }
        }

        public void ShowSuccess(string message)
        {
            AnsiConsole.MarkupLine($"[green]{message}[/]");
        }

        public void ShowWarning(string message)
        {
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
        }

        public void ShowInfo(string message)
        {
            AnsiConsole.MarkupLine($"[blue]{message}[/]");
        }

        private string GetQualityString(QobuzAlbum album)
        {
            var maxBitDepth = album.MaximumBitDepth ?? 16;
            var maxSampleRate = album.MaximumSampleRate ?? 44100;
            
            if (maxBitDepth >= 24 && maxSampleRate >= 192000)
            {
                return $"Hi-Res FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
            }
            else if (maxBitDepth >= 24 && maxSampleRate >= 96000)
            {
                return $"Hi-Res FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
            }
            else if (maxBitDepth >= 16 && maxSampleRate >= 44100)
            {
                return $"FLAC {maxBitDepth}bit/{maxSampleRate / 1000}kHz";
            }
            else
            {
                return "MP3 320kbps";
            }
        }
    }
}