using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Simple implementation of duplicate checking using file-based metadata
    /// </summary>
    public class SimpleDuplicateChecker : ISmartDuplicateChecker
    {
        private readonly ILogger<SimpleDuplicateChecker> _logger;
        private readonly IConfigService _configService;

        public SimpleDuplicateChecker(ILogger<SimpleDuplicateChecker> logger, IConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        public async Task<DuplicateCheckResult> CheckForDuplicateAsync(SearchResult track, string proposedPath)
        {
            try
            {
                // First check if metadata exists
                var metadata = await QobuzMetadata.LoadAsync(proposedPath).ConfigureAwait(false);
                if (metadata != null)
                {
                    // We have metadata, check if it matches this track
                    if (metadata.TrackId == track.Id)
                    {
                        // Same track, check if we should upgrade
                        return await CheckExistingTrack(metadata, track, proposedPath).ConfigureAwait(false);
                    }
                }

                // Check if file exists without metadata (legacy downloads or manual copies)
                if (File.Exists(proposedPath))
                {
                    var fileInfo = new FileInfo(proposedPath);
                    
                    // Try to determine quality from file
                    var estimatedQuality = EstimateQualityFromFile(proposedPath, fileInfo.Length);
                    
                    // Check if it's likely a complete file based on size
                    if (IsFileSizeReasonable(fileInfo.Length, (int)(track.Duration ?? 180)))
                    {
                        // Check if we should upgrade based on quality
                        if (await ShouldUpgradeQuality(estimatedQuality, track))
                        {
                            return new DuplicateCheckResult
                            {
                                IsDuplicate = true,
                                ShouldReplace = true,
                                ExistingFilePath = proposedPath,
                                Reason = "Quality upgrade available"
                            };
                        }

                        return new DuplicateCheckResult
                        {
                            IsDuplicate = true,
                            ShouldReplace = false,
                            ExistingFilePath = proposedPath,
                            Reason = "File already exists with acceptable quality"
                        };
                    }
                    else
                    {
                        // Likely partial download
                        return new DuplicateCheckResult
                        {
                            IsDuplicate = true,
                            IsPartial = true,
                            ShouldReplace = true,
                            ExistingFilePath = proposedPath,
                            Reason = $"Partial download detected (size: {fileInfo.Length:N0} bytes)"
                        };
                    }
                }

                // No duplicate found
                return new DuplicateCheckResult
                {
                    IsDuplicate = false,
                    Reason = "No existing file found"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for duplicate: {Path}", proposedPath);
                // On error, assume no duplicate to allow download
                return new DuplicateCheckResult
                {
                    IsDuplicate = false,
                    Reason = $"Error during check: {ex.Message}"
                };
            }
        }

        private async Task<DuplicateCheckResult> CheckExistingTrack(QobuzMetadata metadata, SearchResult newTrack, string filePath)
        {
            // Validate the existing file
            var validation = await ValidateFileIntegrityAsync(filePath, metadata.File?.ExpectedSize).ConfigureAwait(false);
            
            if (!validation.IsValid)
            {
                return new DuplicateCheckResult
                {
                    IsDuplicate = true,
                    IsCorrupted = !validation.SizeValid,
                    IsPartial = validation.SizeValid && !validation.IsValid,
                    ShouldReplace = true,
                    ExistingFilePath = filePath,
                    Reason = validation.Reason
                };
            }

            // File is valid, check if we should upgrade
            var shouldReplace = await ShouldReplaceFileAsync(metadata, newTrack).ConfigureAwait(false);
            
            if (shouldReplace)
            {
                var comparison = CompareQuality(metadata.Quality, CreateQualityInfo(newTrack));
                return new DuplicateCheckResult
                {
                    IsDuplicate = true,
                    ShouldReplace = true,
                    ExistingFilePath = filePath,
                    QualityComparison = comparison,
                    Reason = comparison.UpgradeReason
                };
            }

            return new DuplicateCheckResult
            {
                IsDuplicate = true,
                ShouldReplace = false,
                ExistingFilePath = filePath,
                Reason = "File already exists with same or better quality"
            };
        }

        public async Task<bool> ShouldReplaceFileAsync(QobuzMetadata existingMetadata, SearchResult newTrack)
        {
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            if (!config.EnableQualityUpgrades)
                return false;

            var newQuality = CreateQualityInfo(newTrack);
            var comparison = CompareQuality(existingMetadata.Quality, newQuality);

            return comparison.IsSignificantUpgrade(config.MinQualityDifferencePercent);
        }

        public async Task<FileValidationResult> ValidateFileIntegrityAsync(string filePath, long? expectedSize = null)
        {
            var result = new FileValidationResult();

            if (!File.Exists(filePath))
            {
                result.FileExists = false;
                result.Reason = "File does not exist";
                return result;
            }

            result.FileExists = true;
            var fileInfo = new FileInfo(filePath);
            result.ActualSize = fileInfo.Length;
            result.ExpectedSize = expectedSize;

            // Check file size
            if (expectedSize.HasValue && expectedSize.Value > 0)
            {
                var tolerance = 0.05; // 5% tolerance
                var difference = Math.Abs(fileInfo.Length - expectedSize.Value);
                var toleranceBytes = expectedSize.Value * tolerance;

                result.SizeValid = difference <= toleranceBytes;
                if (!result.SizeValid)
                {
                    result.Reason = $"File size mismatch: {fileInfo.Length:N0} vs expected {expectedSize.Value:N0}";
                    return result;
                }
            }
            else
            {
                // No expected size, just check if file is reasonably sized (not empty or tiny)
                result.SizeValid = fileInfo.Length > 1000; // At least 1KB
            }

            // Quick header validation
            result.HeaderValid = await ValidateFileHeader(filePath).ConfigureAwait(false);
            if (!result.HeaderValid)
            {
                result.Reason = "Invalid file header";
                return result;
            }

            result.IsValid = true;
            result.Reason = "File validation passed";
            return result;
        }

        public async Task RecordDownloadAsync(string filePath, SearchResult track, long fileSize)
        {
            try
            {
                var metadata = new QobuzMetadata
                {
                    TrackId = track.Id,
                    AlbumId = track.AlbumId,
                    DownloadDate = DateTime.UtcNow,
                    Quality = CreateQualityInfo(track),
                    File = new FileMetadata
                    {
                        Size = fileSize,
                        ExpectedSize = fileSize,
                        Checksum = await CalculateQuickChecksum(filePath).ConfigureAwait(false)
                    }
                };

                await metadata.SaveAsync(filePath).ConfigureAwait(false);
                _logger.LogDebug("Recorded download metadata for: {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record download metadata for: {Path}", filePath);
            }
        }

        private QualityInfo CreateQualityInfo(SearchResult track)
        {
            // Extract quality info from track metadata
            var format = track.Format ?? "FLAC";
            var bitDepth = track.BitDepth ?? 16;
            var sampleRate = track.SampleRate ?? 44100;
            var bitrate = track.Bitrate ?? 0;

            var quality = new QualityInfo
            {
                Format = format,
                BitDepth = bitDepth,
                SampleRate = sampleRate,
                Bitrate = bitrate
            };

            quality.QualityScore = QualityInfo.CalculateQualityScore(format, bitDepth, sampleRate, bitrate);
            return quality;
        }

        private QualityComparison CompareQuality(QualityInfo existing, QualityInfo newQuality)
        {
            var comparison = new QualityComparison
            {
                ExistingQuality = existing,
                NewQuality = newQuality,
                QualityDifference = newQuality.QualityScore - existing.QualityScore
            };

            if (existing.QualityScore > 0)
            {
                comparison.PercentImprovement = ((double)comparison.QualityDifference / existing.QualityScore) * 100;
            }

            // Determine upgrade reason
            if (newQuality.Format != existing.Format && newQuality.QualityScore > existing.QualityScore)
            {
                comparison.UpgradeReason = $"Format upgrade: {existing.Format} → {newQuality.Format}";
            }
            else if (newQuality.BitDepth > existing.BitDepth)
            {
                comparison.UpgradeReason = $"Bit depth upgrade: {existing.BitDepth}-bit → {newQuality.BitDepth}-bit";
            }
            else if (newQuality.SampleRate > existing.SampleRate)
            {
                comparison.UpgradeReason = $"Sample rate upgrade: {existing.SampleRate / 1000}kHz → {newQuality.SampleRate / 1000}kHz";
            }
            else if (newQuality.Bitrate > existing.Bitrate)
            {
                comparison.UpgradeReason = $"Bitrate upgrade: {existing.Bitrate}kbps → {newQuality.Bitrate}kbps";
            }
            else
            {
                comparison.UpgradeReason = "General quality improvement";
            }

            return comparison;
        }

        private bool IsFileSizeReasonable(long actualSize, int durationSeconds)
        {
            // Rough estimation: 1MB per minute for lossy, 5MB per minute for lossless
            var minSizePerMinute = 0.5 * 1024 * 1024; // 0.5 MB/min (very compressed)
            var maxSizePerMinute = 20 * 1024 * 1024;  // 20 MB/min (hi-res)
            
            var durationMinutes = durationSeconds / 60.0;
            var minExpectedSize = (long)(minSizePerMinute * durationMinutes);
            var maxExpectedSize = (long)(maxSizePerMinute * durationMinutes);

            return actualSize >= minExpectedSize && actualSize <= maxExpectedSize;
        }

        private QualityInfo EstimateQualityFromFile(string filePath, long fileSize)
        {
            // This is a rough estimation based on file extension and size
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            
            var quality = new QualityInfo
            {
                Format = extension switch
                {
                    ".flac" => "FLAC",
                    ".mp3" => "MP3",
                    ".m4a" => "M4A",
                    ".opus" => "OPUS",
                    ".ogg" => "OGG",
                    _ => "Unknown"
                }
            };

            // Very rough quality estimation
            if (quality.Format == "FLAC")
            {
                quality.BitDepth = 16; // Conservative estimate
                quality.SampleRate = 44100;
            }
            else
            {
                quality.Bitrate = 320; // Assume high quality lossy
            }

            quality.QualityScore = QualityInfo.CalculateQualityScore(
                quality.Format, quality.BitDepth, quality.SampleRate, quality.Bitrate);

            return quality;
        }

        private async Task<bool> ValidateFileHeader(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var header = new byte[4];
                await stream.ReadAsync(header, 0, 4).ConfigureAwait(false);

                // Check for common audio file headers
                if (header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43) // "fLaC"
                    return true;
                if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0) // MP3 frame sync
                    return true;
                if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) // ID3
                    return true;

                // For other formats, assume valid if file is not empty
                return stream.Length > 1000;
            }
            catch
            {
                // If we can't read the header, assume it's valid
                return true;
            }
        }

        private async Task<string> CalculateQuickChecksum(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buffer = new byte[2048]; // First and last 1KB
                
                // Read first 1KB
                await stream.ReadAsync(buffer, 0, 1024).ConfigureAwait(false);
                
                // Read last 1KB if file is large enough
                if (stream.Length > 2048)
                {
                    stream.Seek(-1024, SeekOrigin.End);
                    await stream.ReadAsync(buffer, 1024, 1024).ConfigureAwait(false);
                }

                // Simple hash of the buffer
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(buffer);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "unknown";
            }
        }

        private async Task<bool> ShouldUpgradeQuality(QualityInfo existing, SearchResult newTrack)
        {
            var newQuality = CreateQualityInfo(newTrack);
            var comparison = CompareQuality(existing, newQuality);
            
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            return comparison.IsSignificantUpgrade(config.MinQualityDifferencePercent);
        }
    }
}