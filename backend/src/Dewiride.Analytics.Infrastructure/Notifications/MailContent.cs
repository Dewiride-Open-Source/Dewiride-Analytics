namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// What one message says, before it is given a shape.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the rendering so that the two forms every message is sent in — HTML and plain
/// text — are written from one description rather than composed twice. Composing them separately
/// is how the plain text quietly stops matching, and the plain text is what a reader whose client
/// refuses HTML actually sees.
/// </para>
/// <para>
/// Every message this product sends is about somebody's account and asks them to do exactly one
/// thing. That is why there is one action rather than a list: a message offering two things to do
/// gets neither done.
/// </para>
/// </remarks>
public sealed record MailContent
{
    /// <summary>
    /// The subject line, which is also the heading at the top of the message.
    /// </summary>
    /// <remarks>
    /// One line for both, so that what somebody read in their inbox is what greets them when they
    /// open it. A subject that promises something the message then words differently is how a
    /// reader starts wondering whether it is genuine.
    /// </remarks>
    public required string Subject { get; init; }

    /// <summary>
    /// The line an inbox shows beside the subject, before anybody opens anything.
    /// </summary>
    /// <remarks>
    /// Written rather than left to the client, which otherwise takes the first words of the body —
    /// "Hi Jagdish," for every message this product has ever sent. It is the second most-read line
    /// in any message and the one most often left to chance.
    /// </remarks>
    public required string Preheader { get; init; }

    /// <summary>What the message says, one paragraph per entry.</summary>
    public required IReadOnlyList<string> Paragraphs { get; init; }

    /// <summary>What the one thing to do is called.</summary>
    public required string Action { get; init; }

    /// <summary>Where that one thing happens.</summary>
    public required string Link { get; init; }

    /// <summary>
    /// Figures the message is about, where it is about figures.
    /// </summary>
    /// <remarks>
    /// A number belongs in a row with its name beside it rather than in the middle of a sentence,
    /// where a reader has to parse prose to find the one thing they opened the message for.
    /// </remarks>
    public IReadOnlyList<MailFact> Facts { get; init; } = [];

    /// <summary>
    /// The small print: how long a link lasts, and what to do if the message was not expected.
    /// </summary>
    /// <remarks>
    /// Kept apart from the paragraphs because it is read differently — skimmed, or read closely by
    /// somebody who is worried. Setting it below the action and in a quieter voice is what lets
    /// the paragraphs stay short.
    /// </remarks>
    public IReadOnlyList<string> Footnotes { get; init; } = [];
}

/// <summary>
/// One figure, and what it is.
/// </summary>
/// <param name="Label">What the figure is, in the words somebody would ask for it.</param>
/// <param name="Value">The figure, already written the way it should be read.</param>
public readonly record struct MailFact(string Label, string Value);
