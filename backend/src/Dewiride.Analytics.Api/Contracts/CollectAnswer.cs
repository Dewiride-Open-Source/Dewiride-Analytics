namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// What the collector tells the tracker in answer to a page view.
/// </summary>
/// <remarks>
/// Sent only for a page view stored for a website that records what visitors press, and not
/// declared in its fetch metadata as sent in a mode whose reply cannot be read. Every other report
/// the collector reads, and a page view for a website that records none, that does not exist or is
/// no longer measured, or from an origin the website does not accept, is answered with nothing at
/// all. The tracker listens for presses only once it has this, so a page opened on a website that
/// records none sends none.
/// </remarks>
/// <param name="Presses">Whether the website records presses: true wherever this is sent.</param>
public sealed record CollectAnswer(bool Presses);
