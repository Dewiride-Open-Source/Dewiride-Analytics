using System.Collections.Concurrent;
using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A copy of the product whose messages are kept where a test can read them.
/// </summary>
/// <remarks>
/// <para>
/// It shares the running stack's stores, so accounts written through one are visible to the other
/// and the keys that seal a reset token — which live in the control plane rather than on a disk —
/// are the same on both. Only where messages go differs.
/// </para>
/// <para>
/// A whole host rather than a substitute for one service, because what is worth proving here is
/// what somebody receives and what happens when they follow it, and both of those run through the
/// endpoints, the account store and the data-protection keys.
/// </para>
/// </remarks>
internal sealed class MailboxInstall : WebApplicationFactory<Program>
{
    private readonly string _controlPlane;
    private readonly string _telemetry;

    private MailboxInstall(string controlPlane, string telemetry)
    {
        _controlPlane = controlPlane;
        _telemetry = telemetry;
    }

    /// <summary>The address this installation says it is published on.</summary>
    public const string PublicAddress = "https://analytics.example";

    /// <summary>Everything this installation has tried to send.</summary>
    public Mailbox Mailbox { get; } = new();

    /// <summary>
    /// Brings a host up against the running stack, with its messages kept rather than logged.
    /// </summary>
    /// <param name="stack">The running stack, whose stores are shared.</param>
    /// <returns>The host, ready to answer.</returns>
    public static MailboxInstall Start(AnalyticsStackFixture stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var install = new MailboxInstall(stack.ControlPlaneConnectionString, stack.TelemetryConnectionString);

        _ = install.Services;

        return install;
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

        // Without this the engine has no address to build a link on and writes a relative one,
        // which is honest but is not what a deployment sends.
        builder.UseSetting(TestSettings.PublicAddress, PublicAddress);

        // Runs after the product has registered its own, so this is the one that answers.
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Mailbox));
    }
}

/// <summary>
/// Keeps every message instead of sending it.
/// </summary>
internal sealed class Mailbox : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _received = new();

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _received.Enqueue(message);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The most recent message sent to an address, or nothing if none was.
    /// </summary>
    /// <param name="address">The mailbox to look in.</param>
    /// <returns>The message.</returns>
    public EmailMessage? LastTo(string address) =>
        _received.LastOrDefault(message =>
            string.Equals(message.ToAddress, address, StringComparison.OrdinalIgnoreCase));

    /// <summary>How many messages have been sent to an address.</summary>
    /// <param name="address">The mailbox to look in.</param>
    /// <returns>The count.</returns>
    public int CountTo(string address) =>
        _received.Count(message =>
            string.Equals(message.ToAddress, address, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads the link out of a message the way somebody clicking it would.
/// </summary>
internal static class ResetLink
{
    /// <summary>
    /// The address the message points at.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The link.</returns>
    public static Uri In(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var start = message.PlainText.IndexOf("https://", StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, "the message has to carry a link somebody can open");

        var end = message.PlainText.IndexOfAny([' ', '\r', '\n'], start);
        var link = end < 0 ? message.PlainText[start..] : message.PlainText[start..end];

        return new Uri(link);
    }

    /// <summary>
    /// The token the link carries.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The token, exactly as a browser would hand it back.</returns>
    public static string TokenIn(EmailMessage message) => Value(In(message), "token");

    /// <summary>
    /// The address the link says it was sent to.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The address.</returns>
    public static string AddressIn(EmailMessage message) => Value(In(message), "address");

    private static string Value(Uri link, string name)
    {
        var pair = link.Query.TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => string.Equals(part[0], name, StringComparison.Ordinal));

        pair.Should().NotBeNull("the link has to carry {0}", name);

        return Uri.UnescapeDataString(pair[1]);
    }
}
