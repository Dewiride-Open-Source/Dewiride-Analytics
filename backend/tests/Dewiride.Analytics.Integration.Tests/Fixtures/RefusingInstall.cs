using System.Collections.Concurrent;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A copy of the product whose edition refuses to let named accounts be closed, restored or deleted.
/// </summary>
/// <remarks>
/// <para>
/// The open-source edition registers nothing alongside closing an account, so the only way to
/// reach the path where an edition says no — and to make one account's deletion fail without
/// touching the stores — is to answer the way an edition with an arrangement to end would when
/// it cannot. What is being proved is the open-source half: that a refusal leaves the account as
/// it was, and that a failure on one account reaches no other.
/// </para>
/// <para>
/// It shares the running stack's stores, so an account closed through one is seen by the other.
/// </para>
/// </remarks>
internal sealed class RefusingInstall : WebApplicationFactory<Program>
{
    private readonly string _controlPlane;
    private readonly string _telemetry;
    private readonly RefusingObserver _observer = new();

    private RefusingInstall(string controlPlane, string telemetry)
    {
        _controlPlane = controlPlane;
        _telemetry = telemetry;
    }

    /// <summary>
    /// Brings a host up against the running stack, with an edition that refuses nothing yet.
    /// </summary>
    /// <param name="stack">The running stack, whose stores are shared.</param>
    /// <returns>The host, ready to answer.</returns>
    public static RefusingInstall Start(AnalyticsStackFixture stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var install = new RefusingInstall(stack.ControlPlaneConnectionString, stack.TelemetryConnectionString);

        _ = install.Services;

        return install;
    }

    /// <summary>Makes the edition refuse every act on an account from now on.</summary>
    /// <param name="organizationId">The account.</param>
    public void Refuse(Guid organizationId) => _observer.Refused[organizationId] = true;

    /// <summary>Lets the edition allow an account again.</summary>
    /// <param name="organizationId">The account.</param>
    public void Allow(Guid organizationId) => _observer.Refused.TryRemove(organizationId, out _);

    /// <summary>
    /// Makes the edition hold every act on an account open until told to let it finish.
    /// </summary>
    /// <remarks>
    /// The edition is asked inside the act's transaction, under the account's lock, so holding it
    /// there keeps the lock held — which is how a test puts two things that must take turns into
    /// the order it wants to prove.
    /// </remarks>
    /// <param name="organizationId">The account.</param>
    /// <returns>The gate: it says when the act has arrived, and lets it go.</returns>
    public Gate Hold(Guid organizationId)
    {
        var gate = new Gate();
        _observer.Held[organizationId] = gate;

        return gate;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureRegistration.ControlPlaneConnectionName}",
            _controlPlane);

        builder.UseSetting(
            $"ConnectionStrings:{ClickHouseRegistration.TelemetryConnectionName}",
            _telemetry);

        builder.UseSetting(TestSettings.SignInAllowance, TestSettings.NoPracticalLimit);
        builder.UseSetting(TestSettings.BackgroundJudging, "false");
        builder.UseSetting(TestSettings.NameChecks, "false");
        builder.UseSetting(TestSettings.CrawlerRangeDownloads, "false");

        // Runs after the product has registered its own, of which there are none: the free product
        // is the one with nothing to do alongside closing an account.
        builder.ConfigureTestServices(services => services.AddSingleton<IAccountClosureObserver>(_observer));
    }

    /// <summary>An edition that refuses whatever it has been told to refuse.</summary>
    private sealed class RefusingObserver : IAccountClosureObserver
    {
        /// <summary>The accounts every act is refused on.</summary>
        public ConcurrentDictionary<Guid, bool> Refused { get; } = new();

        /// <summary>The accounts every act is held open on.</summary>
        public ConcurrentDictionary<Guid, Gate> Held { get; } = new();

        /// <inheritdoc />
        public Task ClosingAsync(Guid organizationId, CancellationToken cancellationToken) =>
            AnswerAsync(organizationId);

        /// <inheritdoc />
        public Task RestoringAsync(Guid organizationId, CancellationToken cancellationToken) =>
            AnswerAsync(organizationId);

        /// <inheritdoc />
        public Task PurgingAsync(Guid organizationId, CancellationToken cancellationToken) =>
            AnswerAsync(organizationId);

        private async Task AnswerAsync(Guid organizationId)
        {
            if (Refused.ContainsKey(organizationId))
            {
                throw new InvalidOperationException("The edition refuses this account.");
            }

            if (Held.TryGetValue(organizationId, out var gate))
            {
                await gate.PassAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// A place an act waits, under whatever it holds, until a test lets it go on.
/// </summary>
internal sealed class Gate
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the act has reached the gate and is waiting.</summary>
    public Task Arrived => _arrived.Task;

    /// <summary>Lets the act go on.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>Records the arrival and waits to be released.</summary>
    public Task PassAsync()
    {
        _arrived.TrySetResult();

        return _released.Task;
    }
}
