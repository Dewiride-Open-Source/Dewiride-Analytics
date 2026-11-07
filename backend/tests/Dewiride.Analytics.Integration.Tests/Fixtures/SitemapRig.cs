using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.Entitlements;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Everything a test about reading a sitemap needs: a control plane of its own, a website on this
/// machine, a clock that can be moved on, and as many engines as it likes.
/// </summary>
/// <remarks>
/// <para>
/// The control plane is its own because a reader takes whichever website's time has come first, across
/// the whole database, and the shared one is full of websites named by tests about something else. It
/// lasts for the run and is emptied before each test.
/// </para>
/// <para>
/// An engine here is the real reader, resolved from the registration the product uses. It has no web
/// host, because nothing about reading goes through one, and that is what lets a test give it settings
/// no installation would be allowed to start with: a gap of a millisecond, or a budget of nothing.
/// </para>
/// </remarks>
internal sealed class SitemapRig : IAsyncDisposable
{
    private static readonly SemaphoreSlim OneCreating = new(1, 1);
    private static string? s_controlPlane;

    private readonly List<ServiceProvider> _engines = [];
    private readonly string _controlPlane;

    private SitemapRig(string controlPlane, LoopbackWebsite website)
    {
        _controlPlane = controlPlane;
        Website = website;
    }

    /// <summary>The website every <c>.test</c> name leads to.</summary>
    public LoopbackWebsite Website { get; }

    /// <summary>The clock a reading's times are written by, which a test can move on.</summary>
    public MovableClock Clock { get; } = new();

    /// <summary>
    /// What a test reads under unless it says otherwise: no gap to wait through, and time for a slow machine.
    /// </summary>
    public static SitemapOptions Quick { get; } = new()
    {
        RequestGap = TimeSpan.FromMilliseconds(1),
        SitemapTimeout = TimeSpan.FromSeconds(30),
        PageTimeout = TimeSpan.FromSeconds(30),
        LargestSitemapBytes = 1024 * 1024,
        PageBytesRead = 64 * 1024,
    };

    /// <summary>
    /// Starts a rig on an empty control plane.
    /// </summary>
    /// <param name="stack">The running stack, whose database server is used.</param>
    /// <returns>The rig.</returns>
    public static async Task<SitemapRig> StartAsync(AnalyticsStackFixture stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var controlPlane = await ControlPlaneAsync(stack).ConfigureAwait(false);

        await using (var database = ContextOn(controlPlane))
        {
            await database.Database
                .ExecuteSqlRawAsync("TRUNCATE organizations CASCADE", Cancellation.Token)
                .ConfigureAwait(false);
        }

        return new SitemapRig(controlPlane, await LoopbackWebsite.StartAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Starts an engine.
    /// </summary>
    /// <param name="settings">What it reads under, or <see cref="Quick"/>.</param>
    /// <param name="configure">Changes anything else about it.</param>
    /// <returns>The engine.</returns>
    public SitemapEngine Engine(SitemapOptions? settings = null, Action<IServiceCollection>? configure = null)
    {
        var chosen = settings ?? Quick;
        var services = new ServiceCollection();

        services.AddDbContext<ControlPlaneDbContext>(options => ConfigureContext(options, _controlPlane));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(chosen));
        services.AddSingleton<IPublicEndpoints>(new LoopbackEndpoints(Website.Port));
        services.AddSingleton(new Pacer(TimeProvider.System, chosen.RequestGap));
        services.AddSingleton<WebsiteFetcher>();
        services.AddSingleton<IMeasurementAllowance, UnmeteredInstallation>();
        services.AddLogging(logging => logging.AddFakeLogging());
        services.AddHybridCache();
        WebsiteHttp.AddClient(services);
        InfrastructureRegistration.AddSitemapReader(services);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();

        _engines.Add(provider);

        return new SitemapEngine(provider);
    }

    /// <summary>
    /// Adds a website, in an account of its own.
    /// </summary>
    /// <param name="domain">What it is registered as.</param>
    /// <param name="sitemap">The address of the sitemap it names.</param>
    /// <param name="timeZoneId">Its reporting zone.</param>
    /// <param name="configure">Applied to the website once it names its sitemap, before it is saved.</param>
    /// <returns>The website.</returns>
    public async Task<Site> AddWebsiteAsync(
        string domain,
        string? sitemap,
        string timeZoneId = "Etc/UTC",
        Action<Site>? configure = null)
    {
        await using var database = ContextOn(_controlPlane);

        var now = Clock.GetUtcNow();
        var organization = new Organization(Guid.NewGuid(), $"Owner of {domain}", now);
        var site = new Site(Guid.NewGuid(), organization.Id, domain, timeZoneId, now);

        site.SetSitemapAddress(sitemap);
        configure?.Invoke(site);

        database.Add(organization);
        database.Add(site);

        await database.SaveChangesAsync(Cancellation.Token).ConfigureAwait(false);

        return site;
    }

    /// <summary>Changes what a website says about its sitemap, as its settings would.</summary>
    /// <param name="siteId">The website.</param>
    /// <param name="address">The new address, or an empty string to clear it.</param>
    /// <returns>A task that completes once it is saved.</returns>
    public async Task ChangeSitemapAsync(Guid siteId, string address)
    {
        await using var database = ContextOn(_controlPlane);

        var engine = Engine();

        await using var scope = engine.Services.CreateAsyncScope();

        var settings = new SiteSettings(database, scope.ServiceProvider.GetRequiredService<HybridCache>());
        var change = await settings
            .ApplyAsync(siteId, new SiteAmendment(null, null, null, address), Cancellation.Token)
            .ConfigureAwait(false);

        change.Outcome.Should().Be(SiteChangeOutcome.Applied);
    }

    /// <summary>Closes the account a website belongs to.</summary>
    /// <param name="site">The website.</param>
    /// <returns>A task that completes once it is closed.</returns>
    public Task CloseAccountAsync(Site site)
    {
        ArgumentNullException.ThrowIfNull(site);

        return SqlAsync($"UPDATE organizations SET closed_at = {Clock.GetUtcNow()} WHERE id = {site.OrganizationId}");
    }

    /// <summary>Removes a website, by way of what removal deletes.</summary>
    /// <param name="siteId">The website.</param>
    /// <returns>A task that completes once it is gone.</returns>
    public Task RemoveWebsiteAsync(Guid siteId) => SqlAsync($"DELETE FROM sites WHERE id = {siteId}");

    /// <summary>
    /// Runs a statement against this rig's control plane, for setting up a state no engine would leave.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <returns>A task that completes once it has run.</returns>
    public async Task SqlAsync(FormattableString statement)
    {
        await using var database = ContextOn(_controlPlane);

        await database.Database.ExecuteSqlAsync(statement, Cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Says a website's time has come, whatever the last pass said about when it would.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task MakeDueAsync(Guid siteId) =>
        SqlAsync($"UPDATE sitemap_reads SET next_pass_at = {DateTimeOffset.UnixEpoch} WHERE site_id = {siteId}");

    /// <summary>Says a claim ran out a moment ago, as it would have if whoever held it had died.</summary>
    /// <param name="siteId">The website.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task ExpireClaimAsync(Guid siteId) =>
        SqlAsync($"UPDATE sitemap_reads SET claimed_until = clock_timestamp() - interval '1 second' WHERE site_id = {siteId}");

    /// <summary>How reading a website stands.</summary>
    /// <param name="siteId">The website.</param>
    /// <returns>Its row, or nothing where it has none.</returns>
    public Task<SitemapRead?> RowAsync(Guid siteId) =>
        InContextAsync(database => database.SitemapReads
            .AsNoTracking()
            .SingleOrDefaultAsync(read => read.SiteId == siteId, Cancellation.Token));

    /// <summary>The files a website's sitemap is made of.</summary>
    /// <param name="siteId">The website.</param>
    /// <returns>Its parts, by their address.</returns>
    public Task<List<SitemapPart>> PartsAsync(Guid siteId) =>
        InContextAsync(database => database.SitemapParts
            .AsNoTracking()
            .Where(part => part.SiteId == siteId)
            .OrderBy(part => part.Address)
            .ToListAsync(Cancellation.Token));

    /// <summary>The pages known for a website.</summary>
    /// <param name="siteId">The website.</param>
    /// <returns>Its pages, by their path.</returns>
    public Task<List<SitePost>> PagesAsync(Guid siteId) =>
        InContextAsync(database => database.SitePosts
            .AsNoTracking()
            .Where(post => post.SiteId == siteId)
            .OrderBy(post => post.Path)
            .ToListAsync(Cancellation.Token));

    /// <summary>One page known for a website.</summary>
    /// <param name="siteId">The website.</param>
    /// <param name="path">Its path.</param>
    /// <returns>The page.</returns>
    public Task<SitePost> PageAsync(Guid siteId, string path) =>
        InContextAsync(database => database.SitePosts
            .AsNoTracking()
            .SingleAsync(post => post.SiteId == siteId && post.Path == path, Cancellation.Token));

    /// <summary>How many times the website has been asked for something.</summary>
    /// <param name="target">The path and query.</param>
    /// <returns>How many times.</returns>
    public int Asked(string target) => Website.Seen.Count(request => request.Target == target);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var engine in _engines)
        {
            await engine.DisposeAsync().ConfigureAwait(false);
        }

        await Website.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// A connection to this rig's control plane for a test to hold for itself, as somebody else changing
    /// something at the same moment would.
    /// </summary>
    /// <returns>A context of its own, which the caller disposes.</returns>
    public ControlPlaneDbContext OwnContext() => ContextOn(_controlPlane);

    private async Task<T> InContextAsync<T>(Func<ControlPlaneDbContext, Task<T>> read)
    {
        await using var database = ContextOn(_controlPlane);

        return await read(database).ConfigureAwait(false);
    }

    private static async Task<string> ControlPlaneAsync(AnalyticsStackFixture stack)
    {
        await OneCreating.WaitAsync(Cancellation.Token).ConfigureAwait(false);

        try
        {
            if (s_controlPlane is not null)
            {
                return s_controlPlane;
            }

            var connection = new NpgsqlConnectionStringBuilder(stack.ControlPlaneConnectionString)
            {
                Database = $"reader_{Guid.NewGuid():n}",
            };

            await using var database = ContextOn(connection.ConnectionString);

            await database.Database.MigrateAsync(Cancellation.Token).ConfigureAwait(false);

            s_controlPlane = connection.ConnectionString;

            return s_controlPlane;
        }
        finally
        {
            OneCreating.Release();
        }
    }

    private static ControlPlaneDbContext ContextOn(string connection)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>();

        ConfigureContext(options, connection);

        return new ControlPlaneDbContext(options.Options);
    }

    private static void ConfigureContext(DbContextOptionsBuilder options, string connection) =>
        options
            .UseNpgsql(connection, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .MigrationsAssembly(InfrastructureRegistration.MigrationsAssemblyName))
            .UseSnakeCaseNamingConvention();
}

/// <summary>
/// One engine, as far as reading sitemaps goes.
/// </summary>
/// <param name="services">Everything it is made of.</param>
internal sealed class SitemapEngine(IServiceProvider services)
{
    /// <summary>Everything it is made of.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>What it runs passes with.</summary>
    public SitemapPass Pass => Services.GetRequiredService<SitemapPass>();

    /// <summary>What it has written in the log.</summary>
    public FakeLogCollector Log => Services.GetFakeLogCollector();

    /// <summary>Runs one pass.</summary>
    /// <returns>Whether there was anything to read.</returns>
    public Task<bool> RunAsync() => Pass.RunNextAsync(Cancellation.Token);

    /// <summary>
    /// Runs something in a scope of its own, as a pass would.
    /// </summary>
    /// <param name="work">What to run.</param>
    /// <returns>A task that completes once it has run.</returns>
    public async Task InScopeAsync(Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = Services.CreateAsyncScope();

        await work(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Claims whatever may be claimed next, as a pass begins by doing.</summary>
    /// <returns>The claim, or nothing.</returns>
    public async Task<SitemapLease?> ClaimAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        var claims = scope.ServiceProvider.GetRequiredService<SitemapClaims>();

        await claims.PrepareAsync(Cancellation.Token).ConfigureAwait(false);

        return await claims.ClaimNextAsync(Cancellation.Token).ConfigureAwait(false);
    }
}

/// <summary>
/// A clock that can be moved on, for a test about what happens an hour from now that does not want to
/// wait an hour.
/// </summary>
/// <remarks>
/// Only the time it tells moves. Its timers go on running in real time, because what they time here is
/// real: the gap before a request reaches a real server, and how long that server gets to answer.
/// </remarks>
internal sealed class MovableClock : TimeProvider
{
    private long _movedOnTicks;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() =>
        base.GetUtcNow() + TimeSpan.FromTicks(Volatile.Read(ref _movedOnTicks));

    /// <summary>Moves it on.</summary>
    /// <param name="by">How far.</param>
    public void MoveOn(TimeSpan by) => Interlocked.Add(ref _movedOnTicks, by.Ticks);
}

/// <summary>
/// An engine that has died, as far as anybody can tell: from the moment it does, nothing more it tries to
/// say reaches the database.
/// </summary>
internal sealed class EngineDeath : DbCommandInterceptor
{
    private volatile bool _dead;

    /// <summary>Ends it.</summary>
    public void Happen() => _dead = true;

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Refuse()
    {
        if (_dead)
        {
            throw new InvalidOperationException("This engine has died.");
        }
    }
}
