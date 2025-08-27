using System;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a Qobuz quality setting with metadata.
    /// </summary>
    public class QobuzQuality
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description => DisplayName;
        public int BitRate { get; set; }
        public bool IsLossless { get; set; }
        public int Priority { get; set; }
        public string Format { get; set; }
        
        // Static instances for common qualities
        public static readonly QobuzQuality Mp3_320 = new() 
        { 
            Id = 5, 
            Name = "MP3 320", 
            DisplayName = "MP3 320kbps", 
            BitRate = 320, 
            IsLossless = false, 
            Priority = 1, 
            Format = "MP3" 
        };
        
        public static readonly QobuzQuality Flac_CD = new() 
        { 
            Id = 6, 
            Name = "FLAC CD", 
            DisplayName = "FLAC CD 16bit/44.1kHz", 
            BitRate = 1411, 
            IsLossless = true, 
            Priority = 2, 
            Format = "FLAC" 
        };
        
        public static readonly QobuzQuality Flac_HiRes_96 = new() 
        { 
            Id = 7, 
            Name = "FLAC Hi-Res 96", 
            DisplayName = "FLAC Hi-Res 24bit/96kHz", 
            BitRate = 4608, 
            IsLossless = true, 
            Priority = 3, 
            Format = "FLAC" 
        };
        
        public static readonly QobuzQuality Flac_HiRes_192 = new() 
        { 
            Id = 27, 
            Name = "FLAC Hi-Res 192", 
            DisplayName = "FLAC Hi-Res 24bit/192kHz", 
            BitRate = 9216, 
            IsLossless = true, 
            Priority = 4, 
            Format = "FLAC" 
        };
        
        /// <summary>
        /// Factory method to create QobuzQuality from ID.
        /// </summary>
        public static QobuzQuality FromId(int id)
        {
            return id switch
            {
                5 => Mp3_320,
                6 => Flac_CD,
                7 => Flac_HiRes_96,
                27 => Flac_HiRes_192,
                _ => new QobuzQuality 
                { 
                    Id = id, 
                    Name = $"Quality {id}", 
                    DisplayName = $"Unknown Quality {id}",
                    BitRate = 0,
                    IsLossless = false,
                    Priority = 0,
                    Format = "Unknown"
                }
            };
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public override bool Equals(object? obj)
        {
            if (obj is QobuzQuality other)
            {
                return Id == other.Id;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}