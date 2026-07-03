using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Authentication;

public class QobuzCredentialFactoryTests
{
    [Fact]
    public void TryFromIndexerSettings_TokenAuth_IgnoresStaleEmailPasswordFields()
    {
        var settings = new QobuzIndexerSettings
        {
            AuthMethod = (int)AuthenticationMethod.Token,
            Email = "old@example.com",
            Password = "old-password",
            UserId = "12345678",
            AuthToken = "token-abcdef0123456789",
            AppId = "123456789",
            AppSecret = "abcdefghijklmnopqrstuvwxyz"
        };

        var credentials = QobuzCredentialFactory.TryFromIndexerSettings(settings);

        credentials.Should().NotBeNull();
        credentials!.Email.Should().BeNull();
        credentials.MD5Password.Should().BeNull();
        credentials.UserId.Should().Be("12345678");
        credentials.AuthToken.Should().Be("token-abcdef0123456789");
        credentials.AppId.Should().Be("123456789");
        credentials.AppSecret.Should().Be("abcdefghijklmnopqrstuvwxyz");
    }

    [Fact]
    public void TryFromIndexerSettings_EmailAuth_HashesPlainPasswordAndIgnoresStaleTokenFields()
    {
        var settings = new QobuzIndexerSettings
        {
            AuthMethod = (int)AuthenticationMethod.Email,
            Email = " listener@example.com ",
            Password = " plain-password ",
            UserId = "12345678",
            AuthToken = "token-abcdef0123456789"
        };

        var credentials = QobuzCredentialFactory.TryFromIndexerSettings(settings);

        credentials.Should().NotBeNull();
        credentials!.Email.Should().Be("listener@example.com");
        credentials.MD5Password.Should().Be(HashingUtility.ComputePasswordMD5Hash("plain-password"));
        credentials.UserId.Should().BeNull();
        credentials.AuthToken.Should().BeNull();
    }
}
