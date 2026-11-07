namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// An address a website is never read from, refused before anything was sent to it.
/// </summary>
/// <remarks>
/// It says why and not which, because which is whatever a stranger's name server answered with, and
/// this can reach a log by way of whatever it was wrapped in.
/// </remarks>
/// <param name="reason">Why the address was refused.</param>
internal sealed class AddressRefusedException(string reason) : IOException(reason);
