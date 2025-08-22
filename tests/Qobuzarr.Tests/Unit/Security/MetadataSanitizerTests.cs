using System;
using FluentAssertions;
using NUnit.Framework;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Security
{
    [TestFixture]
    public class MetadataSanitizerTests
    {
        [Test]
        public void SanitizeVersion_WithNullOrEmpty_ShouldReturnEmpty()
        {
            MetadataSanitizer.SanitizeVersion(null).Should().BeEmpty();
            MetadataSanitizer.SanitizeVersion("").Should().BeEmpty();
            MetadataSanitizer.SanitizeVersion("   ").Should().BeEmpty();
        }

        [Test]
        public void SanitizeVersion_WithCleanInput_ShouldReturnUnchanged()
        {
            var clean = "Deluxe Edition";
            MetadataSanitizer.SanitizeVersion(clean).Should().Be(clean);
            
            clean = "25th Anniversary (Remastered)";
            MetadataSanitizer.SanitizeVersion(clean).Should().Be("25th Anniversary (Remastered)");
        }

        [Test]
        public void SanitizeVersion_WithScriptTag_ShouldRemoveScript()
        {
            var malicious = "<script>alert('XSS')</script>Deluxe";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().NotContain("<script");
            result.Should().NotContain("alert");
            result.Should().NotContain("</script");
            result.Should().Be("Deluxe");
        }

        [Test]
        public void SanitizeVersion_WithJavaScriptUrl_ShouldReturnSafeDefault()
        {
            var malicious = "javascript:alert('XSS')";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().Be("Version"); // Returns safe default when dangerous pattern detected
        }

        [Test]
        public void SanitizeVersion_WithPathTraversal_ShouldRemoveDotDot()
        {
            var malicious = "../../etc/passwd";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().NotContain("..");
            result.Should().Be("___etc_passwd");
        }

        [Test]
        public void SanitizeVersion_WithSqlInjection_ShouldReturnSafeDefault()
        {
            var malicious = "Deluxe'; DROP TABLE albums;--";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().Be("Version"); // Returns safe default when SQL injection detected
        }

        [Test]
        public void SanitizeVersion_WithCommandInjection_ShouldReturnSafeDefault()
        {
            var malicious = "Deluxe && rm -rf /";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().Be("Version"); // Returns safe default when command injection detected
        }

        [Test]
        public void SanitizeVersion_WithNewlinesAndTabs_ShouldNormalizeWhitespace()
        {
            var input = "Deluxe\nEdition\tRemastered\r\n2024";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("Deluxe Edition Remastered 2024");
            result.Should().NotContain("\n");
            result.Should().NotContain("\t");
            result.Should().NotContain("\r");
        }

        [Test]
        public void SanitizeVersion_WithFileSystemDangerousChars_ShouldReplace()
        {
            var input = "Deluxe: Edition / Remastered \\ 2024 * Special?";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("Deluxe- Edition _ Remastered _ 2024 _ Special_");
            result.Should().NotContain(":");
            result.Should().NotContain("/");
            result.Should().NotContain("\\");
            result.Should().NotContain("*");
            result.Should().NotContain("?");
        }

        [Test]
        public void SanitizeVersion_WithControlCharacters_ShouldRemove()
        {
            var input = "Deluxe\x00Edition\x07Remastered\x1F";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("DeluxeEditionRemastered");
        }

        [Test]
        public void SanitizeVersion_WithZeroWidthCharacters_ShouldRemove()
        {
            var input = "Deluxe\u200BEdition\u200C\u200D\uFEFF";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("DeluxeEdition");
        }

        [Test]
        public void SanitizeVersion_WithLongInput_ShouldTruncate()
        {
            var longInput = new string('A', 150);
            var result = MetadataSanitizer.SanitizeVersion(longInput);
            
            result.Length.Should().Be(100);
            result.Should().Be(new string('A', 100));
        }

        [Test]
        public void SanitizeVersion_WithHtmlEntities_ShouldReplace()
        {
            var input = "Deluxe <Edition> & \"Special\" 'Version'";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("Deluxe (Edition) _ 'Special' 'Version'");
        }

        [Test]
        public void SanitizeVersion_WithMultipleSpaces_ShouldCollapse()
        {
            var input = "Deluxe    Edition     Remastered";
            var result = MetadataSanitizer.SanitizeVersion(input);
            
            result.Should().Be("Deluxe Edition Remastered");
        }

        [Test]
        public void SanitizeVersion_WithLdapInjection_ShouldReturnSafeDefault()
        {
            var malicious = "Deluxe)(uid=*)";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().Be("Version"); // Returns safe default when LDAP injection detected
        }

        [Test]
        public void SanitizeVersion_WithXmlInjection_ShouldReturnSafeDefault()
        {
            var malicious = "<!DOCTYPE test SYSTEM \"file:///etc/passwd\">";
            var result = MetadataSanitizer.SanitizeVersion(malicious);
            
            result.Should().Be("Version"); // Returns safe default when XML injection detected
        }

        [Test]
        public void SanitizeArtistName_WithNull_ShouldReturnDefault()
        {
            MetadataSanitizer.SanitizeArtistName(null).Should().Be("Unknown Artist");
            MetadataSanitizer.SanitizeArtistName("").Should().Be("Unknown Artist");
        }

        [Test]
        public void SanitizeArtistName_WithDangerousContent_ShouldSanitize()
        {
            var input = "Artist<script>alert('xss')</script>";
            var result = MetadataSanitizer.SanitizeArtistName(input);
            
            result.Should().Be("Artist");
            result.Should().NotContain("<script");
        }

        [Test]
        public void SanitizeAlbumTitle_WithNull_ShouldReturnDefault()
        {
            MetadataSanitizer.SanitizeAlbumTitle(null).Should().Be("Unknown Album");
            MetadataSanitizer.SanitizeAlbumTitle("").Should().Be("Unknown Album");
        }

        [Test]
        public void SanitizeAlbumTitle_WithDangerousContent_ShouldSanitize()
        {
            var input = "Album../../etc/passwd";
            var result = MetadataSanitizer.SanitizeAlbumTitle(input);
            
            result.Should().Be("Album___etc_passwd");
            result.Should().NotContain("..");
        }

        [Test]
        public void HtmlEncode_ShouldEscapeHtmlChars()
        {
            var input = "<div>Test & \"Quote\" 'Single'</div>";
            var result = MetadataSanitizer.HtmlEncode(input);
            
            result.Should().Be("&lt;div&gt;Test &amp; &quot;Quote&quot; &#39;Single&#39;&lt;&#47;div&gt;");
        }

        [Test]
        public void IsPotentiallyDangerous_WithScriptTag_ShouldReturnTrue()
        {
            MetadataSanitizer.IsPotentiallyDangerous("<script>alert('xss')</script>").Should().BeTrue();
        }

        [Test]
        public void IsPotentiallyDangerous_WithSqlInjection_ShouldReturnTrue()
        {
            MetadataSanitizer.IsPotentiallyDangerous("'; DROP TABLE users;--").Should().BeTrue();
        }

        [Test]
        public void IsPotentiallyDangerous_WithCommandInjection_ShouldReturnTrue()
        {
            MetadataSanitizer.IsPotentiallyDangerous("test && rm -rf /").Should().BeTrue();
        }

        [Test]
        public void IsPotentiallyDangerous_WithCleanInput_ShouldReturnFalse()
        {
            MetadataSanitizer.IsPotentiallyDangerous("Deluxe Edition").Should().BeFalse();
            MetadataSanitizer.IsPotentiallyDangerous("25th Anniversary").Should().BeFalse();
        }

        [Test]
        public void IsPotentiallyDangerous_WithNull_ShouldReturnFalse()
        {
            MetadataSanitizer.IsPotentiallyDangerous(null).Should().BeFalse();
            MetadataSanitizer.IsPotentiallyDangerous("").Should().BeFalse();
        }

        [Test]
        public void SanitizeVersion_WithRealWorldExamples_ShouldHandleCorrectly()
        {
            // Real album version examples
            MetadataSanitizer.SanitizeVersion("Deluxe Edition").Should().Be("Deluxe Edition");
            MetadataSanitizer.SanitizeVersion("(Remastered)").Should().Be("(Remastered)");
            MetadataSanitizer.SanitizeVersion("25th Anniversary Edition").Should().Be("25th Anniversary Edition");
            MetadataSanitizer.SanitizeVersion("Taylor's Version").Should().Be("Taylor's Version");
            MetadataSanitizer.SanitizeVersion("[Explicit]").Should().Be("[Explicit]");
            MetadataSanitizer.SanitizeVersion("Director's Cut").Should().Be("Director's Cut");
            MetadataSanitizer.SanitizeVersion("(Live at Madison Square Garden)").Should().Be("(Live at Madison Square Garden)");
        }
    }
}