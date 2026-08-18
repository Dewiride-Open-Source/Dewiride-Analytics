using System.Collections.Frozen;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Api.Ingest;

/// <summary>
/// The words the collector's published wire format uses, and what they mean.
/// </summary>
/// <remarks>
/// Written out rather than derived from the enumerations, so that renaming a member in C# cannot
/// silently change a format other people's integrations are already sending. Shared by every
/// collection endpoint so that one word never means two things depending on which one received it.
/// </remarks>
internal static class WireVocabulary
{
    /// <summary>What may be reported, by the word used on the wire.</summary>
    public static readonly FrozenDictionary<string, EventKind> Kinds =
        new Dictionary<string, EventKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["pageview"] = EventKind.PageView,
            ["engagement"] = EventKind.Engagement,
            ["exit"] = EventKind.Exit,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The reporters this product ships, by the word each one names itself with.
    /// </summary>
    /// <remarks>
    /// Spelled the way the directory holding each integration is spelled, so that the name in a
    /// request and the folder somebody is reading are visibly the same thing.
    /// </remarks>
    public static readonly FrozenDictionary<string, IngestSurface> Surfaces =
        new Dictionary<string, IngestSurface>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloudflare-worker"] = IngestSurface.CloudflareWorker,
            ["wordpress-plugin"] = IngestSurface.WordPressPlugin,
            ["netlify-edge"] = IngestSurface.NetlifyEdge,
            ["vercel-edge"] = IngestSurface.VercelEdge,
            ["aspnetcore-middleware"] = IngestSurface.AspNetCoreMiddleware,
            ["nextjs-middleware"] = IngestSurface.NextJsMiddleware,
            ["log-import"] = IngestSurface.LogImport,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the reporter a batch says it is.
    /// </summary>
    /// <param name="declared">The word the caller used, if it used one.</param>
    /// <returns>
    /// The named reporter, or <see cref="IngestSurface.ServerSide"/> when nothing recognisable was
    /// named. Unrecognised is not refused: a reporter written against a later release must not
    /// stop being able to report to an earlier engine.
    /// </returns>
    public static IngestSurface ResolveSurface(string? declared) =>
        declared is not null && Surfaces.TryGetValue(declared, out var surface)
            ? surface
            : IngestSurface.ServerSide;
}
