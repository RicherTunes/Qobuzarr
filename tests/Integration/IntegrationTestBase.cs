using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.IntegrationTests;

/// <summary>
/// Base class for integration tests that require live environment.
/// Provides centralized skip handling using Xunit.SkippableFact's Skip.If().
/// </summary>
/// <remarks>
/// Usage pattern:
/// <code>
/// public class MyTests : IntegrationTestBase
/// {
///     public MyTests(ITestOutputHelper output) : base(output) { }
///     
///     public override async Task InitializeAsync()
///     {
///         await base.InitializeAsync();
///         // Your initialization - catch exceptions and call SetSkipReason()
///     }
///     
///     [SkippableFact]  // Note: Use SkippableFact instead of Fact
///     public async Task MyTest()
///     {
///         SkipIfNotReady(); // Call at start of every test
///         // Test code...
///     }
/// }
/// </code>
/// </remarks>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected ITestOutputHelper Output { get; }
    protected LiveLidarrIntegrationFramework? Framework { get; set; }
    private string? _skipReason;

    protected IntegrationTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary>
    /// Sets the skip reason. Call this from InitializeAsync when environment is not ready.
    /// </summary>
    protected void SetSkipReason(string reason)
    {
        _skipReason = reason;
        Output.WriteLine(reason);
    }

    /// <summary>
    /// Returns true if the test should be skipped.
    /// </summary>
    protected bool ShouldSkip => _skipReason != null || Framework == null;

    /// <summary>
    /// Call at the start of every test method. Properly skips with yellow status if not ready.
    /// Requires test to use [SkippableFact] or [SkippableTheory] attribute.
    /// </summary>
    protected void SkipIfNotReady()
    {
        Skip.If(_skipReason != null, _skipReason ?? "Environment not ready");
        Skip.If(Framework == null, "Framework not initialized");
    }

    /// <summary>
    /// Default initialization - creates framework and validates connectivity.
    /// Override to customize, but call base.InitializeAsync() first.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        try
        {
            Framework = new LiveLidarrIntegrationFramework(Output);
            var connectivityResult = await Framework.ValidateBasicConnectivityAsync();
            Output.WriteLine(connectivityResult.ToString());

            if (!connectivityResult.IsSuccess)
            {
                SetSkipReason("⏭️ Skipping: Lidarr not reachable (set LIDARR_URL and LIDARR_API_KEY)");
            }
        }
        catch (IntegrationTestSkipException ex)
        {
            SetSkipReason(ex.Message);
        }
        catch (Exception ex)
        {
            SetSkipReason($"⏭️ Skipping: Live integration not configured ({ex.Message})");
        }
    }

    public virtual Task DisposeAsync()
    {
        Framework?.Dispose();
        return Task.CompletedTask;
    }
}
