using System.ComponentModel.DataAnnotations;

namespace Dewiride.Analytics.Application.Dashboard;

/// <summary>
/// What this installation knows about the screens people sign in to.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Dashboard";

    /// <summary>
    /// Attempts to sign in, or to set an install up, allowed from one network address in five
    /// minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Locking an account after repeated failures stops somebody working through passwords for
    /// one account. This stops them working through accounts instead, which lockout cannot see
    /// because each attempt lands on a different one.
    /// </para>
    /// <para>
    /// Configurable because an address is a poor proxy for a person: a household is one address,
    /// and so is an office of two hundred people behind one connection. Ten is comfortably more
    /// than mistyping produces and far less than guessing needs.
    /// </para>
    /// </remarks>
    [Range(1, 100000)]
    public int SignInAttemptsPerFiveMinutes { get; init; } = 10;

    /// <summary>
    /// Readings of the present moment allowed to one signed-in person in a minute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The screen that answers who is on a site renews itself every ten seconds, so six a minute is
    /// what watching costs — and another six for each visitor opened on it, since a trail is renewed
    /// on the same beat for as long as its row is open. Somebody reading a busy half hour with a
    /// handful of visitors open, in two windows, spends several times what one screen alone does.
    /// </para>
    /// <para>
    /// The allowance is set well above that rather than close to it, because what it protects
    /// against is a signed-in account turned into a load generator, and what it must never do is
    /// break a screen for somebody using it as intended. How long any one of these readings may
    /// occupy the store is bounded separately and on the store's own terms.
    /// </para>
    /// <para>
    /// Counted per person rather than per address, since an office of two hundred behind one
    /// connection would otherwise ration each other out of a screen they are each entitled to.
    /// </para>
    /// </remarks>
    [Range(1, 100000)]
    public int LiveReadingsPerMinute { get; init; } = 300;

    /// <summary>
    /// The address people open this installation on, used to build the links sent by email.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configured and never derived from the request that asked for the link. A hostname taken
    /// from an incoming request is written by whoever sent it, so a reset link built that way can
    /// be aimed at a server the attacker controls — and the person who clicks it has done nothing
    /// wrong and has no way to tell.
    /// </para>
    /// <para>
    /// Read as text rather than as an address, because an installation that sends no mail never
    /// needs one and every way of not setting it — absent, blank, a variable that expanded to
    /// nothing — has to mean the same thing. What was written is turned into an address by
    /// <see cref="PublishedAt"/>, and anything that is not one is a refusal to start.
    /// </para>
    /// </remarks>
    public string? PublicAddress { get; init; }

    /// <summary>
    /// The configured address, when what was configured is somewhere a browser could be sent.
    /// </summary>
    /// <remarks>
    /// A hostname on its own is not: it reads correctly in a settings file and then produces a
    /// link that goes nowhere. Nothing is guessed on its behalf — a value that is not a whole
    /// address is answered here as nothing at all, and the validation on this section turns that
    /// into a refusal to start with the key named.
    /// </remarks>
    public Uri? PublishedAt =>
        Uri.TryCreate(PublicAddress, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            ? address
            : null;
}
