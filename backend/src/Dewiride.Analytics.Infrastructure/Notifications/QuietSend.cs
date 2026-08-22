using System.Diagnostics.CodeAnalysis;
using Dewiride.Analytics.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// Hands a message to a mail server and treats a refusal as the mail server's problem.
/// </summary>
/// <remarks>
/// <para>
/// Every message this product sends is a side effect of something the caller has already been
/// answered about, or is about to be answered about identically whatever happens here. Asking for
/// a way back into an account and creating one both answer the same whether or not the address is
/// known, and a fault that escaped would be answered differently — which would say, to anybody who
/// cared to look, which addresses have accounts on this installation.
/// </para>
/// <para>
/// Written once because the alternative is this catch, and the two suppressions that go with it,
/// copied into every place that sends anything.
/// </para>
/// </remarks>
public static class QuietSend
{
    /// <summary>
    /// Sends a message, or reports that it could not be handed over.
    /// </summary>
    /// <param name="email">How messages leave the building.</param>
    /// <param name="message">The message.</param>
    /// <param name="logger">Log, which receives the failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> where a mail server took it.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Delivery goes through a port, so the failures possible here belong to "
            + "whatever implements it and cannot be enumerated from this side. One that escaped "
            + "would turn a deliberately uniform answer into a signal that an address exists.")]
    [SuppressMessage(
        "Major Code Smell",
        "S2221:\"Exception\" should not be caught when not required by called methods",
        Justification = "Delivery goes through a port, so the failures possible here belong to "
            + "whatever implements it and cannot be enumerated from this side. One that escaped "
            + "would turn a deliberately uniform answer into a signal that an address exists.")]
    public static async Task<bool> TryAsync(
        IEmailSender email,
        EmailMessage message,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await email.SendAsync(message, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmailLog.CouldNotHandOver(logger, message.Subject, exception);

            return false;
        }
    }
}
