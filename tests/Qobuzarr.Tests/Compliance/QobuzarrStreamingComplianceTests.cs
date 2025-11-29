using System;
using System.Linq;
using System.Reflection;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Xunit;

namespace Qobuzarr.Tests.Compliance;

/// <summary>
/// Streaming service compliance tests for Qobuzarr.
/// These tests verify Qobuzarr implements all required streaming service patterns.
/// </summary>
[Trait("Category", "Compliance")]
[Trait("Category", "Streaming")]
public class QobuzarrStreamingComplianceTests : IDisposable
{
    private readonly Assembly _pluginAssembly;
    private readonly Type _authServiceType;
    private readonly Type _indexerType;
    private readonly Type _downloadClientType;

    public QobuzarrStreamingComplianceTests()
    {
        _pluginAssembly = typeof(QobuzIndexer).Assembly;
        _authServiceType = typeof(QobuzAuthenticationService);
        _indexerType = typeof(QobuzIndexer);
        _downloadClientType = typeof(QobuzDownloadClient);
    }

    #region Authentication Tests

    [Fact]
    public void Authentication_ServiceExists()
    {
        Assert.NotNull(_authServiceType);
    }

    [Fact]
    public void Authentication_HasAuthenticateMethod()
    {
        var methods = _authServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasAuthenticate = methods.Any(m =>
            m.Name.Contains("Authenticate", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Login", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasAuthenticate, "Authentication service should have an Authenticate/Login method");
    }

    [Fact]
    public void Authentication_HasSessionManagement()
    {
        var methods = _authServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasSessionMethods = methods.Any(m =>
            m.Name.Contains("Session", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSessionMethods, "Authentication service should have session management methods");
    }

    [Fact]
    public void Authentication_HasValidationMethod()
    {
        var methods = _authServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasValidate = methods.Any(m =>
            m.Name.Contains("Validate", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("IsAuthenticated", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Check", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasValidate, "Authentication service should have a validation method");
    }

    [Fact]
    public void Authentication_ImplementsSharedInterfaces()
    {
        var interfaces = _authServiceType.GetInterfaces();
        var hasStreamingInterface = interfaces.Any(i =>
            i.Name.Contains("Streaming", StringComparison.OrdinalIgnoreCase) ||
            i.Name.Contains("IQobuz", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasStreamingInterface, "Authentication service should implement streaming/Qobuz interfaces");
    }

    #endregion

    #region Indexer Tests

    [Fact]
    public void Indexer_Exists()
    {
        Assert.NotNull(_indexerType);
    }

    [Fact]
    public void Indexer_HasNameProperty()
    {
        var nameProperty = _indexerType.GetProperty("Name");
        Assert.NotNull(nameProperty);
    }

    [Fact]
    public void Indexer_HasProtocolProperty()
    {
        var protocolProperty = _indexerType.GetProperty("Protocol");
        Assert.NotNull(protocolProperty);
    }

    [Fact]
    public void Indexer_SupportsSearch()
    {
        var supportsSearchProperty = _indexerType.GetProperty("SupportsSearch");
        Assert.NotNull(supportsSearchProperty);
    }

    [Fact]
    public void Indexer_HasAsyncMethods()
    {
        var methods = _indexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var asyncMethods = methods.Where(m =>
            m.ReturnType.IsGenericType &&
            (m.ReturnType.GetGenericTypeDefinition().Name.Contains("Task") ||
             m.ReturnType.GetGenericTypeDefinition().Name.Contains("ValueTask")));

        Assert.NotEmpty(asyncMethods);
    }

    [Fact]
    public void Indexer_HasRequestGenerator()
    {
        var methods = _indexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasRequestGenerator = methods.Any(m =>
            m.Name.Contains("RequestGenerator", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("GetRequest", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasRequestGenerator, "Indexer should have request generator capability");
    }

    [Fact]
    public void Indexer_HasParser()
    {
        var methods = _indexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasParser = methods.Any(m =>
            m.Name.Contains("Parser", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("GetParser", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasParser, "Indexer should have parser capability");
    }

    #endregion

    #region Download Client Tests

    [Fact]
    public void DownloadClient_Exists()
    {
        Assert.NotNull(_downloadClientType);
    }

    [Fact]
    public void DownloadClient_HasDownloadMethod()
    {
        var methods = _downloadClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasDownload = methods.Any(m =>
            m.Name.Contains("Download", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasDownload, "Download client must implement a Download method");
    }

    [Fact]
    public void DownloadClient_HasGetItemsMethod()
    {
        var methods = _downloadClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasGetItems = methods.Any(m =>
            m.Name.Contains("GetItems", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Queue", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasGetItems, "Download client should implement GetItems method");
    }

    [Fact]
    public void DownloadClient_HasStatusMethod()
    {
        var methods = _downloadClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasStatus = methods.Any(m =>
            m.Name.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("GetStatus", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasStatus, "Download client should implement GetStatus method");
    }

    [Fact]
    public void DownloadClient_HasRemoveMethod()
    {
        var methods = _downloadClientType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var hasRemove = methods.Any(m =>
            m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasRemove, "Download client should implement RemoveItem method");
    }

    [Fact]
    public void DownloadClient_ImplementsIDisposable()
    {
        var interfaces = _downloadClientType.GetInterfaces();
        var isDisposable = interfaces.Any(i => i == typeof(IDisposable));

        Assert.True(isDisposable, "Download client should implement IDisposable");
    }

    #endregion

    #region Infrastructure Tests

    [Fact]
    public void Infrastructure_HasRateLimiting()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasRateLimiter = allTypes.Any(t =>
            t.Name.Contains("RateLimiter", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Throttle", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Adaptive", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasRateLimiter, "Qobuz plugin should implement rate limiting");
    }

    [Fact]
    public void Infrastructure_HasCaching()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasCaching = allTypes.Any(t =>
            t.Name.Contains("Cache", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasCaching, "Qobuz plugin should implement response caching");
    }

    [Fact]
    public void Infrastructure_HasExceptionTypes()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var exceptionTypes = allTypes.Where(t =>
            typeof(Exception).IsAssignableFrom(t) &&
            !t.IsAbstract &&
            t != typeof(Exception)).ToList();

        Assert.NotEmpty(exceptionTypes);
    }

    [Fact]
    public void Infrastructure_HasApiClient()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasApiClient = allTypes.Any(t =>
            t.Name.Contains("ApiClient", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("QobuzClient", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasApiClient, "Qobuz plugin should have an API client");
    }

    [Fact]
    public void Infrastructure_HasConcurrencyManagement()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasConcurrency = allTypes.Any(t =>
            t.Name.Contains("Concurrency", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Semaphore", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Parallel", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasConcurrency, "Qobuz plugin should have concurrency management");
    }

    #endregion

    #region Qobuz-Specific Tests

    [Fact]
    public void Qobuz_HasQualitySupport()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasQuality = allTypes.Any(t =>
            t.Name.Contains("Quality", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasQuality, "Qobuz plugin should support audio quality selection");
    }

    [Fact]
    public void Qobuz_HasMLOptimization()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasML = allTypes.Any(t =>
            t.Name.Contains("ML", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("MachineLearning", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Optimizer", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasML, "Qobuz plugin should support ML optimization");
    }

    [Fact]
    public void Qobuz_HasSessionModel()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasSession = allTypes.Any(t =>
            t.Name.Contains("Session", StringComparison.OrdinalIgnoreCase) &&
            t.Namespace?.Contains("Authentication", StringComparison.OrdinalIgnoreCase) == true);

        Assert.True(hasSession, "Qobuz plugin should have session model");
    }

    [Fact]
    public void Qobuz_HasCredentialsModel()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasCredentials = allTypes.Any(t =>
            t.Name.Contains("Credentials", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Login", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasCredentials, "Qobuz plugin should have credentials model");
    }

    [Fact]
    public void Qobuz_HasDownloadOrchestration()
    {
        var allTypes = _pluginAssembly.GetTypes();
        var hasOrchestration = allTypes.Any(t =>
            t.Name.Contains("Orchestrat", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Batch", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasOrchestration, "Qobuz plugin should have download orchestration");
    }

    #endregion

    public void Dispose()
    {
        // Cleanup if needed
    }
}
