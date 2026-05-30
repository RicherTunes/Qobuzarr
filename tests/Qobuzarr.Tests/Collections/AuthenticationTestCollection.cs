using Xunit;

namespace Qobuzarr.Tests.Collections;

/// <summary>
/// Serializes all <see cref="Qobuzarr.Authentication.QobuzAuthenticationService"/>-touching
/// tests across <see cref="QobuzAuthenticationServiceCovTests"/> and
/// <see cref="QobuzAuthenticationServiceTests"/>.
///
/// Why: the production service initializes an internal file-backed
/// <c>_persistentStore</c> when no override is injected — and the tests don't
/// override it. Two test classes running concurrently both write+read the same
/// session file, so <c>GetCachedSession_WithExpiredSession_ShouldReturnNull</c>
/// occasionally sees a valid-not-yet-expired session written by another test
/// instance. Symptom: ~1-in-3 runs report 1-2 failures in these classes.
///
/// xUnit serializes tests within a single Collection. By attributing both
/// classes with <c>[Collection(AuthenticationTestCollection.Name)]</c>, we
/// force sequential execution and mask the race. The underlying isolation
/// (inject an in-memory store per test) is tracked as separate tech debt —
/// see CLAUDE.md "Flaky Tests Policy".
///
/// This collection also serializes the NLog-capture tests
/// (<c>QobuzAppSecretLogScrubTests</c>, <c>E2EHermeticGateTests</c>): both capture the
/// process-global NLog <c>MemoryTarget</c> via <c>TestLogger</c> and one calls
/// <c>ClearLoggedMessages()</c>, so running them in parallel races (one test's Clear erases
/// the other's captured logs). Same shared-global-state class of problem, same fix.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthenticationTestCollection
{
    public const string Name = "QobuzAuthentication";
}
