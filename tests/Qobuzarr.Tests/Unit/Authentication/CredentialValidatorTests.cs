using System;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Unit.Authentication;

[Trait("Category", "Unit")]
public class CredentialValidatorTests
{
    private readonly CredentialValidator _validator = new(LogManager.GetCurrentClassLogger());

    #region ValidateCredentials — null / empty

    [Fact]
    public void ValidateCredentials_Null_ReturnsInvalid()
    {
        var result = _validator.ValidateCredentials(null!);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCredentials_EmptyCredentials_ReturnsInvalid()
    {
        var result = _validator.ValidateCredentials(new QobuzCredentials());
        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidateCredentials — email auth

    [Fact]
    public void ValidateCredentials_ValidEmailAuth_IsValid()
    {
        var creds = new QobuzCredentials
        {
            Email = "testuser@qobuz.com",
            MD5Password = "d41d8cd98f00b204e9800998ecf8427e",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateCredentials_InvalidEmail_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            Email = "not-an-email",
            MD5Password = "d41d8cd98f00b204e9800998ecf8427e",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCredentials_EmptyPassword_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            Email = "testuser@qobuz.com",
            MD5Password = "",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidateCredentials — token auth

    [Fact]
    public void ValidateCredentials_ValidTokenAuth_IsValid()
    {
        var creds = new QobuzCredentials
        {
            UserId = "12345678",
            AuthToken = "abcdef0123456789abcdef0123456789",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateCredentials_EmptyUserId_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            UserId = "",
            AuthToken = "abcdef0123456789abcdef0123456789",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidateCredentials — security (injection)

    [Fact]
    public void ValidateCredentials_SqlInjectionInEmail_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            Email = "testuser@qobuz.com'; DROP TABLE users;--",
            MD5Password = "d41d8cd98f00b204e9800998ecf8427e",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateCredentials_XssInToken_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            UserId = "12345678",
            AuthToken = "<script>alert('xss')</script>",
            AppId = "123456789",
            AppSecret = "abcdef0123456789abcdef0123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidateCredentials — app credentials

    [Fact]
    public void ValidateCredentials_EmptyAppId_IsValidWithDefault()
    {
        var creds = new QobuzCredentials
        {
            Email = "testuser@qobuz.com",
            MD5Password = "d41d8cd98f00b204e9800998ecf8427e",
            AppId = ""
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCredentials_AppIdWithoutSecret_ReturnsError()
    {
        var creds = new QobuzCredentials
        {
            Email = "testuser@qobuz.com",
            MD5Password = "d41d8cd98f00b204e9800998ecf8427e",
            AppId = "123456789"
        };

        var result = _validator.ValidateCredentials(creds);
        Assert.False(result.IsValid);
    }

    #endregion

    #region Result model

    [Fact]
    public void CredentialValidationResult_Merge_CombinesErrorsAndWarnings()
    {
        var a = new CredentialValidationResult();
        a.AddError("Error A");
        a.AddWarning("Warn A");

        var b = new CredentialValidationResult();
        b.AddError("Error B");

        a.Merge(b);

        Assert.Equal(2, a.Errors.Count);
        Assert.Single(a.Warnings);
        Assert.False(a.IsValid);
    }

    [Fact]
    public void CredentialValidationResult_NoErrors_IsValid()
    {
        var result = new CredentialValidationResult();
        Assert.True(result.IsValid);
    }

    #endregion
}
