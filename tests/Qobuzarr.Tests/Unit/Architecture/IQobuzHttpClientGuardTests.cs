using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Xunit;

namespace Qobuzarr.Tests.Unit.Architecture;

public class IQobuzHttpClientGuardTests
{
    [Fact]
    public void In_Plugin_Assembly_Exactly_One_IQobuzHttpClient_Exists_And_Is_Api_Http_Variant()
    {
        var pluginAssembly = typeof(QobuzHttpClient).Assembly;

        var interfaces = pluginAssembly.GetTypes()
            .Where(t => t is { IsInterface: true, Name: "IQobuzHttpClient" })
            .ToList();

        interfaces.Should().HaveCount(1,
            "in the plugin assembly, exactly one IQobuzHttpClient interface should exist to prevent confusion");
        interfaces[0].Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.API.Http",
            "the single IQobuzHttpClient should be the internal API HTTP abstraction");
    }

    [Fact]
    public void Should_Have_IJsonHttpClient_For_Abstractions_Layer()
    {
        var type = typeof(IJsonHttpClient);
        type.IsInterface.Should().BeTrue();
        type.Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.Abstractions");
    }
}

