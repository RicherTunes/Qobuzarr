using System;
using System.IO;
using FluentAssertions;
using Lidarr.Plugin.Common.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public class AudioMagicBytesValidatorTests
    {
        [Theory]
        [InlineData(new byte[] { 0x66, 0x4C, 0x61, 0x43 })] // fLaC
        [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53 })] // OggS
        [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46 })] // RIFF
        [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04 })] // ID3
        [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64 })] // MPEG frame sync
        public void IsValidAudioMagicBytes_WithKnownHeaders_ShouldReturnTrue(byte[] bytes)
        {
            AudioMagicBytesValidator.IsValidAudioMagicBytes(bytes).Should().BeTrue();
        }

        [Theory]
        [InlineData(new byte[] { 0x3C, 0x21, 0x44, 0x4F })] // <!DO (HTML)
        [InlineData(new byte[] { 0x7B, 0x22, 0x65, 0x72 })] // {"er (JSON)
        public void IsValidAudioMagicBytes_WithNonAudioHeaders_ShouldReturnFalse(byte[] bytes)
        {
            AudioMagicBytesValidator.IsValidAudioMagicBytes(bytes).Should().BeFalse();
        }

        [Fact]
        public void ValidateAudioMagicBytes_WithValidFile_ShouldNotThrow()
        {
            var path = Path.Combine(Path.GetTempPath(), $"qobuzarr-magic-{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(path, new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x01, 0x02 });
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
                File.WriteAllBytes(path, new byte[] { 0x66, 0x4C, 0x61 }); // < 4 bytes
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
