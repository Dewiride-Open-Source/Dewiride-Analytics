using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Dewiride.Analytics.Api.Configuration;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Endpoints;
using Dewiride.Analytics.Api.Ingest;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Application.Persistence;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// Registers the services that belong to the web surface itself.
/// </summary>
internal static class ApiRegistration
{
    /// <summary>
    /// Adds request limits, cross-origin rules, forwarded-address trust and the API description.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHostApplicationBuilder AddApiServices(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The header names the server and its version to anything that connects, which is a
        // starting point for someone deciding what to try next and buys nothing in return.
        builder.Services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        builder.Services.AddOptions<CollectorOptions>()
            .Bind(builder.Configuration.GetSection(CollectorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<ClassificationOptions>()
            .Bind(builder.Configuration.GetSection(ClassificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<SchemaOptions>()
            .Bind(builder.Configuration.GetSection(SchemaOptions.SectionName))
            .ValidateOnStart();

        var email = Bind<EmailOptions>(builder, EmailOptions.SectionName);

        builder.Services.AddOptions<DashboardOptions>()
            .Bind(builder.Configuration.GetSection(DashboardOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.PublicAddress)
                    || settings.PublishedAt is not null,
                $"{DashboardOptions.SectionName}:PublicAddress must be the whole address people "
                + "open the dashboard on, such as https://analytics.example.com.")
            .Validate(
                settings => !email.Enabled || settings.PublishedAt is not null,
                $"{DashboardOptions.SectionName}:PublicAddress must be set when "
                + $"{EmailOptions.SectionName}:Enabled is true. The links in the messages this "
                + "product sends are built from it, and are deliberately never taken from the "
                + "address a request claims to have arrived at.")
            .ValidateOnStart();

        var collector = Bind<CollectorOptions>(builder, CollectorOptions.SectionName);
        var dashboard = Bind<DashboardOptions>(builder, DashboardOptions.SectionName);
        var network = Bind<NetworkOptions>(builder, NetworkOptions.SectionName);

        ConfigureForwardedHeaders(builder.Services, network);
        ConfigureCrossOrigin(builder.Services);
        ConfigureRateLimiting(builder.Services, collector, dashboard);

        ConfigureCallerIdentity(builder.Services);
        ConfigureSerialisation(builder.Services);

        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();

        return builder;
    }

    /// <summary>
    /// Puts the compile-time serialisation plan ahead of the reflection-based one.
    /// </summary>
    /// <remarks>
    /// Inserted rather than substituted, so a payload the plan does not cover — a problem
    /// document, for instance — still serialises through the resolver behind it instead of
    /// failing at run time.
    /// </remarks>
    private static void ConfigureSerialisation(IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

    /// <summary>
    /// Supplies the identity of whoever is making the current request.
    /// </summary>
    /// <remarks>
    /// Authorisation asks who the caller is through a port that knows nothing about HTTP, so the
    /// one implementation that does know is registered here rather than in the layer that reaches
    /// the database. Outside a request there is no caller, and the port answers that honestly.
    /// </remarks>
    private static void ConfigureCallerIdentity(IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentPrincipalAccessor>(provider =>
        {
            var requests = provider.GetRequiredService<IHttpContextAccessor>();

            return new ClaimsPrincipalAccessor(() => requests.HttpContext?.User);
        });
    }

    /// <summary>
    /// Reads a section eagerly, for settings needed while the container is still being built.
    /// </summary>
    private static T Bind<T>(IHostApplicationBuilder builder, string sectionName)
        where T : new() =>
        builder.Configuration.GetSection(sectionName).Get<T>() ?? new T();

    /// <summary>
    /// Declares which upstream hops may be believed about a visitor's address.
    /// </summary>
    /// <remarks>
    /// With nothing declared the middleware is left switched off entirely, so the address is the
    /// one the connection came from. That is the safe default: an address read from a header
    /// anybody can write would flow straight into the visitor key and the network attribution.
    /// </remarks>
    private static void ConfigureForwardedHeaders(IServiceCollection services, NetworkOptions network)
    {
        if (!network.HasTrustedHops)
        {
            return;
        }

        var proxies = network.TrustedProxies.Select(value => Parse(value, IPAddress.Parse)).ToArray();
        var networks = network.TrustedNetworks.Select(value => Parse(value, System.Net.IPNetwork.Parse)).ToArray();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = network.ForwardLimit;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var range in networks)
            {
                options.KnownIPNetworks.Add(range);
            }
        });
    }

    private static T Parse<T>(string value, Func<string, T> parse)
    {
        try
        {
            return parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{value}' in {NetworkOptions.SectionName} is not a valid address or address range."),
                exception);
        }
    }

    /// <summary>
    /// Opens the collector to every origin.
    /// </summary>
    /// <remarks>
    /// It has to be: the tracker runs on the customer's own domain, and no list of domains kept
    /// here could ever be complete. Which site a request may report for is decided from the site's
    /// own declared origins once the body has been read, which is a check on the data rather than
    /// on the browser's willingness to enforce one.
    /// </remarks>
    [SuppressMessage(
        "Sonar",
        "S5122:Authorizing an origin is security-sensitive",
        Justification = "The policy is attached to the collector alone, which is unauthenticated, "
            + "sends no credentials and returns an empty body. There is nothing for a cross-origin "
            + "reader to obtain, and the origin a report may be filed under is checked against the "
            + "site's own declared list after the body is read. Every other endpoint is same-origin.")]
    private static void ConfigureCrossOrigin(IServiceCollection services) =>
        services.AddCors(options => options.AddPolicy(
            CollectEndpoint.CorsPolicyName,
            policy => policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .WithMethods(HttpMethods.Post)));

    /// <summary>How long the sign-in allowance is counted over.</summary>
    private static readonly TimeSpan SignInWindow = TimeSpan.FromMinutes(5);

    private static void ConfigureRateLimiting(
        IServiceCollection services,
        CollectorOptions collector,
        DashboardOptions dashboard) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(
                CollectEndpoint.RateLimitPolicyName,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    RequestObservation.ClientAddress(context) ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = collector.RequestsPerMinutePerAddress,
                        Window = TimeSpan.FromMinutes(1),
                        // Reports are worthless once they are late, so a caller over the limit is
                        // turned away rather than held: queuing would spend memory on a request
                        // whose answer nobody is waiting for.
                        QueueLimit = 0,
                    }));

            limiter.AddPolicy(
                ServerCollectEndpoint.RateLimitPolicyName,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    RequestObservation.ClientAddress(context) ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = collector.ServerBatchesPerMinutePerAddress,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            limiter.AddPolicy(
                RateLimitPolicies.Accounts,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    RequestObservation.ClientAddress(context) ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = dashboard.SignInAttemptsPerFiveMinutes,
                        Window = SignInWindow,
                        QueueLimit = 0,
                    }));
        });
}
