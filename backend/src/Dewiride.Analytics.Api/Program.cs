using Dewiride.Analytics.Api.Composition;
using Dewiride.Analytics.Api.Endpoints;
using Dewiride.Analytics.Api.Observability;
using Dewiride.Analytics.Api.Startup;
using Dewiride.Analytics.Application;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.AddApiServices();
builder.AddAuthentication();
builder.AddApplicationServices();
builder.AddControlPlane(AuthenticationRegistration.AddSignIn);
builder.AddTelemetryStore();

// Which edition this is was decided by which projects were compiled. The host finds the module
// that came with it rather than naming one, so neither edition's code is present in the other.
var edition = builder.AddEdition();

builder.Services.AddHostedService<SchemaMigrationService>();

var app = builder.Build();

// Before anything reads the caller's address. Switched off unless an upstream hop has been
// declared trustworthy, in which case this is where a forwarded address replaces the connection's.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapCollect();
app.MapAccount();
app.MapSites();
app.MapHealth();

// The description covers a contract that is published in the open anyway, and a self-hoster
// reaching for it is usually trying to find out why an integration is not reporting.
app.MapOpenApi().AllowAnonymous();

StartupLog.EditionStarting(app.Logger, edition.EditionName);

await app.RunAsync();
