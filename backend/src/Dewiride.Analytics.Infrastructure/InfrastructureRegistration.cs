using System.Net;
using Dewiride.Analytics.Application.Abstractions;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Infrastructure.Accounts;
using Dewiride.Analytics.Infrastructure.Classification;
using Dewiride.Analytics.Infrastructure.Crawlers;
using Dewiride.Analytics.Infrastructure.Health;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Ingest;
using Dewiride.Analytics.Infrastructure.Network;
using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Infrastructure.Sites;
using Dewiride.Analytics.Infrastructure.Telemetry;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure;

/// <summary>
/// Registers the control-plane database and the services built on it.
/// </summary>
public static class InfrastructureRegistration
{
    /// <summary>
    /// Configuration key holding the PostgreSQL connection string.
    /// </summary>
    public const string ControlPlaneConnectionName = "ControlPlane";

    /// <summary>
    /// Name the data-protection key ring is filed under.
    /// </summary>
    /// <remarks>
    /// Set explicitly rather than derived from the content root, which is what the framework
    /// falls back to. A derived name changes when the application is moved or repackaged, and
    /// every cookie issued under the old name stops being readable — a silent mass sign-out on
    /// an upgrade that changed nothing else.
    /// </remarks>
    public const string ProtectionRingName = "Dewiride.Analytics";

    /// <summary>
    /// Assembly the control-plane migrations are compiled into.
    /// </summary>
    /// <remarks>
    /// Named rather than referenced because the dependency runs the other way: the migrations
    /// project references this one. The host references the migrations project, which is what
    /// puts the assembly in the load context at run time.
    /// </remarks>
    public const string MigrationsAssemblyName = "Dewiride.Analytics.Migrations.Postgres";

    /// <summary>
    /// How many turns one asking of a website can have to wait for inside its own time: each redirect, and
    /// the robots.txt of the website's other name on the way.
    /// </summary>
    private const int TurnsInOneAsking = WebsiteFetcher.MostRedirects + 1;

    /// <summary>
    /// Adds the control-plane database, the account store, the site catalogue and visitor-key
    /// derivation.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configureAccounts">
    /// Runs against the account store once it has been registered, so that the host can add the
    /// parts of sign-in that depend on there being a request to sign in to. See
    /// <see cref="AddAccounts"/> for why they are not added here.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static IHostApplicationBuilder AddControlPlane(
        this IHostApplicationBuilder builder,
        Action<IdentityBuilder> configureAccounts)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureAccounts);

        var connectionString = builder.Configuration.GetConnectionString(ControlPlaneConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ControlPlaneConnectionName}' is not configured. "
                + "Set ConnectionStrings__ControlPlane in the environment.");

        builder.Services.AddDbContext<ControlPlaneDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .MigrationsAssembly(MigrationsAssemblyName))
            .UseSnakeCaseNamingConvention());

        configureAccounts(AddAccounts(builder.Services));
        AddCookieProtection(builder.Services);

        builder.Services.AddHybridCache();

        // Registered by whichever component needs it first, so that each is self-sufficient and
        // a test can still substitute a controlled clock for the whole host.
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddScoped<ISiteCatalog, CachedSiteCatalog>();
        builder.Services.AddScoped<ISiteDirectory, SiteDirectory>();
        builder.Services.AddScoped<ISiteRoster, SiteRoster>();
        builder.Services.AddScoped<ISiteSettings, SiteSettings>();
        builder.Services.AddScoped<IIngestKeyCatalog, CachedIngestKeyCatalog>();
        builder.Services.AddScoped<IIngestKeyDirectory, IngestKeyDirectory>();
        builder.Services.AddScoped<IOrganizationDirectory, OrganizationDirectory>();
        builder.Services.AddScoped<IInstallation, Installation>();
        builder.Services.AddScoped<IAccountProfile, AccountProfile>();
        builder.Services.AddScoped<IInvitations, Invitations>();
        builder.Services.AddScoped<IPasswordReset, PasswordReset>();
        builder.Services.AddScoped<OrganizationOwners>();
        builder.Services.AddScoped<IAccountClosure, AccountClosure>();

        // Registered here rather than by an edition. Which grants exist differs between an
        // installation with one organisation and a service with many; what a grant permits does
        // not, and an edition able to answer that more generously than the other would be a
        // security advisory rather than a feature difference.
        builder.Services.AddScoped<ITenantScopeProvider, MembershipTenantScopeProvider>();
        builder.Services.AddScoped<IClassificationProgressStore, ClassificationProgressStore>();

        builder.Services.AddSingleton<VisitorKeySaltStore>();
        builder.Services.AddSingleton<IVisitorKeyFactory, RotatingSaltVisitorKeyFactory>();
        builder.Services.AddHostedService<VisitorKeySaltRotationService>();

        // One memory for the process. A visitor is held for as long as a visit lasts, which is the
        // judging engine's own measure of when one is over, so a visit turned away at the door and a
        // visit judged later end at the same moment.
        builder.Services.AddSingleton<IIgnoredVisitors>(services => new MemoryIgnoredVisitors(
            services.GetRequiredService<IOptions<ClassificationOptions>>().Value.IdleTimeout,
            MemoryIgnoredVisitors.MostRemembered,
            services.GetRequiredService<TimeProvider>()));

        builder.Services.AddSingleton<ClosedAccountPurge>();
        builder.Services.AddHostedService<ClosedAccountPurgeService>();

        AddVisitorContext(builder);
        AddNotifications(builder);
        AddSitemapReading(builder);

        builder.Services.AddHealthChecks()
            .AddCheck<ControlPlaneHealthCheck>(
                ControlPlaneHealthCheck.Name,
                tags: [HealthCheckTags.Readiness]);

        return builder;
    }

    /// <summary>
    /// Adds the lookup that turns a visitor's address into a place and a network, and the service
    /// that keeps its data current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lookup is a singleton holding a memory-mapped database, and is asked once per accepted
    /// event. Anything shorter-lived would re-open a hundred and twenty megabytes per page view.
    /// </para>
    /// <para>
    /// The download client keeps the framework's own default timeout, which bounds reaching the
    /// server and getting a reply out of it. It deliberately does not bound reading that reply:
    /// the refresher takes the response as soon as its headers arrive, and the timeout is
    /// documented as applying only up to that point. What bounds the hundred and twenty megabytes
    /// afterwards is <see cref="ReferenceDataOptions.DownloadTimeout"/>, which is stated in
    /// minutes and is the one somebody can raise.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddVisitorContext(IHostApplicationBuilder builder)
    {
        // Checked when the host starts rather than when the first refresh runs, so a setting
        // somebody mistyped in an environment file is a refusal to start with a sentence
        // explaining it, and not a service that silently never resolves anybody's country.
        builder.Services.AddOptions<ReferenceDataOptions>()
            .Bind(builder.Configuration.GetSection(ReferenceDataOptions.SectionName))
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Directory),
                $"{ReferenceDataOptions.SectionName}:Directory must name a directory to keep the "
                + "downloaded visitor reference data in.")
            .Validate(
                settings => IsFetchable(settings.PlacesUrl) && IsFetchable(settings.NetworksUrl),
                $"{ReferenceDataOptions.SectionName}:PlacesUrl and NetworksUrl must both be "
                + "absolute http or https addresses.")
            .Validate(
                settings => IsARefreshInterval(settings.RefreshInterval),
                $"{ReferenceDataOptions.SectionName}:RefreshInterval must be between five minutes "
                + "and 49 days. The data behind it is republished monthly and hourly.")
            .Validate(
                settings => settings.DownloadTimeout >= TimeSpan.FromSeconds(30),
                $"{ReferenceDataOptions.SectionName}:DownloadTimeout must be at least thirty "
                + "seconds. The place database is around a hundred and twenty megabytes.")
            .ValidateOnStart();

        builder.Services.AddHttpClient(ReferenceDataRefresher.HttpClientName);

        builder.Services.AddSingleton<ReferenceDataStore>();
        builder.Services.AddSingleton<INetworkLookup, ReferenceDataNetworkLookup>();
        builder.Services.AddHostedService<ReferenceDataRefresher>();

        AddCrawlerAddresses(builder);
    }

    /// <summary>
    /// Whether a refresh can wait this long: not so often that it hammers whoever publishes the data,
    /// and no longer than a timer can wait, which is a little over 49 days. A service asked to wait
    /// longer fails as it starts, and takes the engine with it.
    /// </summary>
    private static bool IsARefreshInterval(TimeSpan interval) =>
        interval >= TimeSpan.FromMinutes(5) && interval <= TimeSpan.FromDays(49);

    /// <summary>
    /// Adds the lookup that recognises an address a company vouches for as its own crawlers', and
    /// the service that keeps the published lists current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A singleton for the same reason the place lookup is one: it is asked once per accepted
    /// event, and it holds a table built from a dozen files that would otherwise be reread for
    /// every page view.
    /// </para>
    /// <para>
    /// The download client is capped rather than left to buffer whatever arrives. These files come
    /// from other companies' web servers, and the largest of them is a few tens of kilobytes — so
    /// anything approaching the cap is a redirect to something that is not a list of addresses, and
    /// should cost a failed refresh rather than the machine's memory.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddCrawlerAddresses(IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<CrawlerRangeOptions>()
            .Bind(builder.Configuration.GetSection(CrawlerRangeOptions.SectionName))
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Directory),
                $"{CrawlerRangeOptions.SectionName}:Directory must name a directory to keep the "
                + "published crawler address ranges in.")
            .Validate(
                settings => IsARefreshInterval(settings.RefreshInterval),
                $"{CrawlerRangeOptions.SectionName}:RefreshInterval must be between five minutes and "
                + "49 days. The lists behind it are republished no more often than daily.")
            .Validate(
                settings => settings.DownloadTimeout >= TimeSpan.FromSeconds(5),
                $"{CrawlerRangeOptions.SectionName}:DownloadTimeout must be at least five seconds.")
            .Validate(
                settings => settings.LargestFileBytes >= 64 * 1024,
                $"{CrawlerRangeOptions.SectionName}:LargestFileBytes must be at least 64 kilobytes. "
                + "The largest of these lists is a few tens of kilobytes and grows.")
            .ValidateOnStart();

        builder.Services.AddHttpClient(
            CrawlerRangeRefresher.HttpClientName,
            (services, client) =>
                client.MaxResponseContentBufferSize =
                    services.GetRequiredService<IOptions<CrawlerRangeOptions>>().Value.LargestFileBytes);

        builder.Services.AddSingleton<CrawlerRangeStore>();
        builder.Services.AddSingleton<ICrawlerAddressDirectory>(
            services => services.GetRequiredService<CrawlerRangeStore>());
        builder.Services.AddHostedService<CrawlerRangeRefresher>();

        AddCrawlerNames(builder);
    }

    /// <summary>
    /// Adds the check that settles whose crawlers an address belongs to from the name it answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A singleton because what it remembers is the point of it. The answers outlive any one pass
    /// over a site's history, and a fresh instance per pass would ask a name server the same
    /// question about the same fleet every half hour.
    /// </para>
    /// <para>
    /// It is the one thing registered here that reaches the network while answering, and the only
    /// caller is the background pass that judges finished visits. Nothing on the ingest path may
    /// take a dependency on it.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddCrawlerNames(IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<CrawlerNameOptions>()
            .Bind(builder.Configuration.GetSection(CrawlerNameOptions.SectionName))
            .Validate(
                settings => settings.LookupTimeout >= TimeSpan.FromSeconds(1),
                $"{CrawlerNameOptions.SectionName}:LookupTimeout must be at least one second. It "
                + "covers two questions put to somebody else's name server.")
            .Validate(
                settings => settings.LookupsAtOnce is >= 1 and <= 64,
                $"{CrawlerNameOptions.SectionName}:LookupsAtOnce must be between one and sixty-four.")
            .Validate(
                settings => settings.RememberFor >= TimeSpan.FromMinutes(5),
                $"{CrawlerNameOptions.SectionName}:RememberFor must be at least five minutes, or a "
                + "single busy crawler becomes a stream of questions to its operator's name server.")
            .Validate(
                settings => settings.RememberedAnswers >= 1000,
                $"{CrawlerNameOptions.SectionName}:RememberedAnswers must be at least one thousand.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IReverseNameLookup, SystemNameLookup>();
        builder.Services.AddSingleton<ICrawlerNameLookup, CrawlerNameLookup>();
    }

    /// <summary>
    /// Adds the way messages leave the building.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which implementation is registered is a deployment decision rather than a per-request one,
    /// so it is read here and settled for the life of the process. Everything else about the mail
    /// server is read at the moment of each send.
    /// </para>
    /// <para>
    /// An installation with no mail server still gets a sender. Somebody who has forgotten the
    /// password to their own analytics and cannot be sent a way back in is locked out for good,
    /// and that is a defect rather than a consequence of not running a mail server.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddNotifications(IHostApplicationBuilder builder)
    {
        // Checked when the host starts rather than at the first send, which is somebody already
        // locked out waiting for a message that a mistyped port means will never arrive.
        builder.Services.AddOptions<EmailOptions>()
            .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                settings => !settings.Enabled || !string.IsNullOrWhiteSpace(settings.Host),
                $"{EmailOptions.SectionName}:Host must name the mail server to hand messages to.")
            .Validate(
                settings => !settings.Enabled || Addressable(settings.FromAddress),
                $"{EmailOptions.SectionName}:FromAddress must be the address messages are sent "
                + "from, such as analytics@example.com.")
            .Validate(
                settings => settings.Port is > 0 and < 65536,
                $"{EmailOptions.SectionName}:Port must be a port number.")
            .Validate(
                settings => settings.Timeout >= TimeSpan.FromSeconds(5),
                $"{EmailOptions.SectionName}:Timeout must be at least five seconds. A mail server "
                + "that has to look a hostname up takes longer than an instant to answer.")
            .ValidateOnStart();

        var configured = builder.Configuration
            .GetSection(EmailOptions.SectionName)
            .Get<EmailOptions>() ?? new EmailOptions();

        if (configured.Enabled)
        {
            builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
    }

    /// <summary>
    /// Adds what is known about the posts a website lists in its sitemap, and the way out to the
    /// websites themselves.
    /// </summary>
    /// <remarks>
    /// The way out is not configured from anywhere. Where a connection may go is decided by the one
    /// implementation registered here, because a setting able to widen it would also be a setting able
    /// to point the hosted service at its own network.
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddSitemapReading(IHostApplicationBuilder builder)
    {
        // Bound as the host starts, so that a value that is not a switch in an environment file is a
        // refusal to start and not an installation reading sitemaps it was told to leave alone.
        var settings = builder.Services.AddOptions<SitemapOptions>()
            .Bind(builder.Configuration.GetSection(SitemapOptions.SectionName))
            .ValidateOnStart();

        ValidateAsking(settings);
        ValidatePasses(settings);

        builder.Services.AddScoped<ISitePosts, SitePosts>();
        builder.Services.AddScoped<IPostCatalogue, PostCatalogue>();

        builder.Services.AddSingleton<IPublicEndpoints>(new PublicInternetEndpoints(Dns.GetHostAddressesAsync));
        WebsiteHttp.AddClient(builder.Services);

        // One for the process, because the limit it keeps is the website's and not any one reading's.
        builder.Services.AddSingleton(services => new Pacer(
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<IOptions<SitemapOptions>>().Value.RequestGap));
        builder.Services.AddSingleton<WebsiteFetcher>();

        AddSitemapReader(builder.Services);
        builder.Services.AddHostedService<SitemapReadingService>();
    }

    /// <summary>
    /// Adds what one pass over a website is made of, given the way out to websites.
    /// </summary>
    /// <remarks>
    /// One scope to a pass, so that nothing of one website's can ride along into the next website's, and
    /// its database connection is only open while a statement is. The pass goes off to a website for
    /// minutes at a time and holds nothing while it does.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    internal static void AddSitemapReader(IServiceCollection services)
    {
        services.AddSingleton<SitemapPass>();
        services.AddScoped<SitemapClaims>();
        services.AddScoped<PostWriter>();
        services.AddScoped<SitemapCycle>();
        services.AddScoped<PageReader>();
    }

    /// <summary>
    /// Refuses what no website should be asked under, and what no timer could keep.
    /// </summary>
    private static void ValidateAsking(OptionsBuilder<SitemapOptions> settings) =>
        settings
            .Validate(
                sitemaps => sitemaps.RequestGap >= TimeSpan.FromSeconds(1) && IsAWaitATimerCanKeep(sitemaps.RequestGap),
                $"{SitemapOptions.SectionName}:RequestGap must be at least one second, and no more than 49 "
                + "days. The website being read did not ask to be, and pays for every request.")
            .Validate(
                sitemaps => sitemaps.RequestGap < ShortestAsking(sitemaps) / TurnsInOneAsking,
                $"{SitemapOptions.SectionName}:RequestGap must leave time to follow a website's redirects: "
                + $"{WebsiteFetcher.MostRedirects} of them and a robots.txt on the way each wait that long, inside both "
                + $"{SitemapOptions.SectionName}:PageTimeout and {SitemapOptions.SectionName}:SitemapTimeout.")
            .Validate(
                sitemaps => sitemaps.ConnectTimeout >= TimeSpan.FromSeconds(1)
                    && sitemaps.ConnectTimeout <= TimeSpan.FromSeconds(60),
                $"{SitemapOptions.SectionName}:ConnectTimeout must be between one second and a minute.")
            .Validate(
                sitemaps => IsAWaitATimerCanKeep(sitemaps.SitemapTimeout),
                $"{SitemapOptions.SectionName}:SitemapTimeout must be no more than 49 days.")
            .Validate(
                sitemaps => IsAWaitATimerCanKeep(sitemaps.PageTimeout),
                $"{SitemapOptions.SectionName}:PageTimeout must be no more than 49 days.")
            .Validate(
                sitemaps => sitemaps.LargestSitemapBytes is >= 1024 * 1024 and <= SitemapOptions.ProtocolLimitBytes,
                $"{SitemapOptions.SectionName}:LargestSitemapBytes must be between one mebibyte and "
                + $"{SitemapOptions.ProtocolLimitBytes} bytes, which is as much as the protocol lets one file hold.")
            .Validate(
                sitemaps => sitemaps.PageBytesRead is >= 64 * 1024 and <= 4 * 1024 * 1024,
                $"{SitemapOptions.SectionName}:PageBytesRead must be between 64 kibibytes and four mebibytes.");

    /// <summary>
    /// Refuses a reader that could not do its job, or could not keep to what a claim promises.
    /// </summary>
    private static void ValidatePasses(OptionsBuilder<SitemapOptions> settings) =>
        settings
            .Validate(
                sitemaps => sitemaps.SitesAtOnce is >= 0 and <= 16,
                $"{SitemapOptions.SectionName}:SitesAtOnce must be between nought, which leaves reading to "
                + "other engines, and sixteen.")
            .Validate(
                sitemaps => sitemaps.Interval >= TimeSpan.FromSeconds(5) && IsAWaitATimerCanKeep(sitemaps.Interval),
                $"{SitemapOptions.SectionName}:Interval must be at least five seconds, and no more than 49 days.")
            .Validate(
                sitemaps => sitemaps.Cadence >= TimeSpan.FromHours(1) && IsAWaitATimerCanKeep(sitemaps.Cadence),
                $"{SitemapOptions.SectionName}:Cadence must be at least an hour, and no more than 49 days.")
            .Validate(
                sitemaps => sitemaps.PagesPerPass is >= 1 and <= 600,
                $"{SitemapOptions.SectionName}:PagesPerPass must be between one and six hundred.")
            .Validate(
                sitemaps => sitemaps.PassBudget >= TimeSpan.FromMinutes(1) && IsAWaitATimerCanKeep(sitemaps.PassBudget),
                $"{SitemapOptions.SectionName}:PassBudget must be at least a minute, and no more than 49 days.")
            .Validate(
                sitemaps => IsAWaitATimerCanKeep(sitemaps.Lease) && sitemaps.Lease >= LeastLease(sitemaps),
                $"{SitemapOptions.SectionName}:Lease must be no more than 49 days, and must outlast the "
                + "pass holding it: its PassBudget, a minute's margin, and asking for robots.txt and a sitemap "
                + "at the last moment, with the RequestGap before each.")
            .Validate(
                sitemaps => sitemaps.MostSitemapParts is >= 1 and <= 50_000,
                $"{SitemapOptions.SectionName}:MostSitemapParts must be between one and 50,000, which is as "
                + "many as the protocol lets an index name.")
            .Validate(
                sitemaps => sitemaps.MostPostsPerSite is >= 1_000 and <= 1_000_000,
                $"{SitemapOptions.SectionName}:MostPostsPerSite must be between 1,000 and 1,000,000.")
            .Validate(
                sitemaps => sitemaps.RetryAfterFailure >= TimeSpan.FromMinutes(5)
                    && IsAWaitATimerCanKeep(sitemaps.RetryAfterFailure),
                $"{SitemapOptions.SectionName}:RetryAfterFailure must be at least five minutes, and no more "
                + "than 49 days.");

    /// <summary>The shorter of the two times one asking of a website can have.</summary>
    private static TimeSpan ShortestAsking(SitemapOptions settings) =>
        settings.PageTimeout < settings.SitemapTimeout ? settings.PageTimeout : settings.SitemapTimeout;

    /// <summary>
    /// How long a pass can run for, at the most. It starts nothing new once its budget is spent, and the
    /// worst it can have just started is a website's robots.txt followed by a file of its sitemap, each
    /// of which waits its turn before its own time starts. A redirect partway through that file can read the
    /// robots.txt of the website's other name, and does so in the time the file has.
    /// </summary>
    private static TimeSpan LeastLease(SitemapOptions settings) =>
        settings.PassBudget
        + settings.PageTimeout
        + settings.SitemapTimeout
        + (settings.RequestGap * 2)
        + TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether a timer can keep a wait this long, which is a little over 49 days. A longer one fails
    /// when it is set, which would be partway through reading somebody's sitemap.
    /// </summary>
    private static bool IsAWaitATimerCanKeep(TimeSpan wait) => wait <= TimeSpan.FromDays(49);

    /// <summary>
    /// Whether a value could be a mailbox rather than something somebody meant to fill in later.
    /// </summary>
    /// <remarks>
    /// Deliberately not a full check of what an address may contain. The mail server decides that,
    /// and a stricter rule here would only turn away the unusual addresses that are nevertheless
    /// perfectly valid.
    /// </remarks>
    private static bool Addressable(string address) =>
        address.Contains('@', StringComparison.Ordinal)
        && !address.StartsWith('@')
        && !address.EndsWith('@');

    /// <summary>
    /// Whether an address is one the refresher could actually fetch from.
    /// </summary>
    /// <remarks>
    /// A file path would be accepted by a looser check and then fail every night in a log nobody
    /// reads. Both defaults are public addresses, and an installation pointing these at a mirror
    /// of its own is still pointing them at a web server.
    /// </remarks>
    private static bool IsFetchable(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Adds the account store and the authorisation-server store.
    /// </summary>
    /// <remarks>
    /// Only the stores and the password rules that belong to them. Sign-in
    /// schemes, the cookies they issue and the authorisation-server endpoints are the host's
    /// concern: they all rest on ASP.NET Core, and keeping that out of this assembly is what
    /// stops request-level concerns drifting into the layer that talks to the database. The host
    /// adds them through the callback this returns to.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The account store, for the host to build sign-in on.</returns>
    private static IdentityBuilder AddAccounts(IServiceCollection services)
    {
        var accounts = services
            .AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ControlPlaneDbContext>();

        // Length alone lets through fifteen characters of one repeated key, so the rule the
        // options do not express is expressed here instead.
        services.AddScoped<IPasswordValidator<ApplicationUser>, PredictablePasswordValidator>();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ControlPlaneDbContext>()
                .ReplaceDefaultEntities<Guid>());

        return accounts;
    }

    /// <summary>
    /// Keeps the keys that protect sign-in cookies in the database.
    /// </summary>
    /// <remarks>
    /// The default is a folder inside the running container, which does not survive being
    /// replaced: every signed-in person would be signed out by a restart, and a second instance
    /// of the API could not read a cookie the first one issued. Putting them in PostgreSQL, which
    /// is backed up because everything else in the control plane lives there, fixes both.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    private static void AddCookieProtection(IServiceCollection services) =>
        services.AddDataProtection()
            .SetApplicationName(ProtectionRingName)
            .PersistKeysToDbContext<ControlPlaneDbContext>();

    /// <summary>
    /// Sets the password, lockout and account policies.
    /// </summary>
    /// <param name="options">Identity options to configure.</param>
    private static void ConfigureIdentity(IdentityOptions options)
    {
        // Length only, no character-class requirements. NIST SP 800-63B-4 §3.1.1.2 states that
        // verifiers SHALL NOT impose composition rules, because people satisfy them with
        // predictable substitutions that cost an attacker nothing, and sets fifteen characters as
        // the minimum where a password is the only authenticator — which is the case here until
        // app-based two-step verification ships. The same section asks for a blocklist, which is
        // what PredictablePasswordValidator is.
        // https://pages.nist.gov/800-63-4/sp800-63b/passwords/
        options.Password.RequiredLength = 15;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
    }
}
