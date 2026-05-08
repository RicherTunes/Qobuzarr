using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Lidarr.Plugin.Common.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public class AudioMagicBytesValidatorTests
    {
        private static readonly byte[] FlacMagic = Encoding.ASCII.GetBytes("fLaC");
        private static readonly byte[] OggMagic = Encoding.ASCII.GetBytes("OggS");
        private static readonly byte[] RiffMagic = Encoding.ASCII.GetBytes("RIFF");
        private static readonly byte[] Id3Magic = Encoding.ASCII.GetBytes("ID3");

        [Theory]
        [MemberData(nameof(ValidAudioHeaders))]
        public void IsValidAudioMagicBytes_WithKnownHeaders_ShouldReturnTrue(byte[] bytes)
        {
            AudioMagicBytesValidator.IsValidAudioMagicBytes(bytes).Should().BeTrue();
        }

        public static TheoryData<byte[]> ValidAudioHeaders => new()
        {
            FlacMagic,
            OggMagic,
            RiffMagic,
            Id3Magic,
            // MPEG frame sync cannot be represented as ASCII; use hex
            new byte[] { 0xFF, 0xFB, 0x90, 0x64 }
        };

        [Theory]
        [MemberData(nameof(InvalidHeaders))]
        public void IsValidAudioMagicBytes_WithNonAudioHeaders_ShouldReturnFalse(byte[] bytes)
        {
            AudioMagicBytesValidator.IsValidAudioMagicBytes(bytes).Should().BeFalse();
        }

        public static TheoryData<byte[]> InvalidHeaders => new()
        {
            Encoding.ASCII.GetBytes("<!DO"), // HTML start
            Encoding.ASCII.GetBytes("{\"er") // JSON start
        };

        [Fact]
        public void ValidateAudioMagicBytes_WithValidFile_ShouldNotThrow()
        {
            var path = Path.Combine(Path.GetTempPath(), $"qobuzarr-magic-{Guid.NewGuid():N}.bin");
            try
            {
                var fileData = new byte[7];
                FlacMagic.CopyTo(fileData, 0);
                File.WriteAllBytes(path, fileData);
                Action act = () => AudioMagicBytesValidator.ValidateAudioMagicBytes(path);
                act.Should().NotThrow();
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void ValidateAudioMagicBytes_WithTooSmallFile_ShouldThrow()
        {
            var path = Path.Combine(Path.GetTempPath(), $"qobuzarr-magic-{Guid.NewGuid():N}.bin");
            try
            {
                // Write only 3 bytes (< 4 required for magic validation)
                File.WriteAllBytes(path, FlacMagic.AsSpan(0, 3).ToArray());
                Action act = () => AudioMagicBytesValidator.ValidateAudioMagicBytes(path);
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("File too small for magic validation*");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
