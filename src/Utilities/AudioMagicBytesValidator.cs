using System;
using System.IO;
using System.Text;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    public static class AudioMagicBytesValidator
    {
        public static void ValidateAudioMagicBytes(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }

            Span<byte> magicBytes = stackalloc byte[4];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var read = fs.Read(magicBytes);
                if (read < 4)
                {
                    throw new InvalidOperationException($"File too small for magic validation: {Path.GetFileName(filePath)}");
                }
            }

            if (!IsValidAudioMagicBytes(magicBytes))
            {
                var hex = BitConverter.ToString(magicBytes.ToArray());
                var ascii = ToAsciiPreview(magicBytes);
                throw new InvalidOperationException($"Invalid audio magic bytes '{hex}' ('{ascii}') for {Path.GetFileName(filePath)}");
            }
        }

        public static bool IsValidAudioMagicBytes(ReadOnlySpan<byte> magicBytes)
        {
            if (magicBytes.Length < 2)
            {
                return false;
            }

            // FLAC: fLaC
            if (magicBytes.Length >= 4 &&
                magicBytes[0] == (byte)'f' &&
                magicBytes[1] == (byte)'L' &&
                magicBytes[2] == (byte)'a' &&
                magicBytes[3] == (byte)'C')
            {
                return true;
            }

            // OGG: OggS
            if (magicBytes.Length >= 4 &&
                magicBytes[0] == (byte)'O' &&
                magicBytes[1] == (byte)'g' &&
                magicBytes[2] == (byte)'g' &&
                magicBytes[3] == (byte)'S')
            {
                return true;
            }

            // WAV/RIFF: RIFF
            if (magicBytes.Length >= 4 &&
                magicBytes[0] == (byte)'R' &&
                magicBytes[1] == (byte)'I' &&
                magicBytes[2] == (byte)'F' &&
                magicBytes[3] == (byte)'F')
            {
                return true;
            }

            // MP3 with ID3 tag: ID3 (3 bytes)
            if (magicBytes.Length >= 3 &&
                magicBytes[0] == (byte)'I' &&
                magicBytes[1] == (byte)'D' &&
                magicBytes[2] == (byte)'3')
            {
                return true;
            }

            // MP3 without ID3: frame sync 0xFFEx
            if (magicBytes[0] == 0xFF && (magicBytes[1] & 0xE0) == 0xE0)
            {
                return true;
            }

            return false;
        }

        private static string ToAsciiPreview(ReadOnlySpan<byte> bytes)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }
            return sb.ToString();
        }
    }
}
