using Dewiride.Analytics.Application.Abstractions;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Infrastructure.Accounts;
using Dewiride.Analytics.Infrastructure.Classification;
using Dewiride.Analytics.Infrastructure.Health;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Network;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Sites;
using Dewiride.Analytics.Infrastructure.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        builder.Services.AddScoped<IInstallation, Installation>();
        builder.Services.AddScoped<IClassificationProgressStore, ClassificationProgressStore>();

        builder.Services.AddSingleton<VisitorKeySaltStore>();
        builder.Services.AddSingleton<IVisitorKeyFactory, RotatingSaltVisitorKeyFactory>();
        builder.Services.AddHostedService<VisitorKeySaltRotationService>();

        AddVisitorContext(builder);

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
                settings => settings.RefreshInterval >= TimeSpan.FromMinutes(5),
                $"{ReferenceDataOptions.SectionName}:RefreshInterval must be at least five "
                + "minutes. The data behind it is republished monthly and hourly.")
            .Validate(
                settings => settings.DownloadTimeout >= TimeSpan.FromSeconds(30),
                $"{ReferenceDataOptions.SectionName}:DownloadTimeout must be at least thirty "
                + "seconds. The place database is around a hundred and twenty megabytes.")
            .ValidateOnStart();

        builder.Services.AddHttpClient(ReferenceDataRefresher.HttpClientName);

        builder.Services.AddSingleton<ReferenceDataStore>();
        builder.Services.AddSingleton<INetworkLookup, ReferenceDataNetworkLookup>();
        builder.Services.AddHostedService<ReferenceDataRefresher>();
    }

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
