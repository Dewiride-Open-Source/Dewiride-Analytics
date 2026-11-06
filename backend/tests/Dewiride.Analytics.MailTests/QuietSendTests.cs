using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// Handing a message over without letting its failure become part of the answer.
/// </summary>
/// <remarks>
/// A password reset answers an address nobody has exactly as it answers one somebody has, and only
/// the second sends a message. A failure that escaped here would tell them apart.
/// </remarks>
public sealed class QuietSendTests
{
    private static readonly EmailMessage Message =
        new("reader@example.com", null, "Subject", "Plain", "<p>Html</p>", "the-same-message");

    /// <summary>A mail server that refuses is written down and goes no further.</summary>
    [Fact]
    public async Task A_Mail_Server_That_Refuses_Goes_No_Further()
    {
        var taken = await QuietSend.TryAsync(
            new Failing(new InvalidOperationException("Refused.")),
            Message,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        taken.Should().BeFalse();
    }

    /// <summary>
    /// A mail server the client stopped waiting for goes no further either: it reaches here as a
    /// cancellation, but nobody asked for one.
    /// </summary>
    [Fact]
    public async Task A_Mail_Server_The_Client_Stopped_Waiting_For_Goes_No_Further()
    {
        var taken = await QuietSend.TryAsync(
            new Failing(new TaskCanceledException("The client stopped waiting.")),
            Message,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        taken.Should().BeFalse();
    }

    /// <summary>Whoever asked going away still ends the send.</summary>
    [Fact]
    public async Task Whoever_Asked_Going_Away_Ends_The_Send()
    {
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        var sending = () => QuietSend.TryAsync(
            new Failing(new OperationCanceledException(gone.Token)),
            Message,
            NullLogger.Instance,
            gone.Token);

        await sending.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>A mail server that fails in one particular way.</summary>
    /// <param name="failure">How it fails.</param>
    private sealed class Failing(Exception failure) : IEmailSender
    {
        /// <inheritdoc />
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }
}
