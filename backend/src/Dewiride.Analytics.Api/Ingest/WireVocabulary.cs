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
            ["action"] = EventKind.Action,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What sort of control a reported word describes.
    /// </summary>
    /// <remarks>
    /// A page describes its own controls, in a vocabulary that is open and partly of its own
    /// invention: it may declare a role for a screen reader, and where it declares none the
    /// element it used has to answer instead. Both spellings are resolved here into the closed
    /// set the store holds, and anything unrecognised resolves to <see cref="ControlKind.Unknown"/>
    /// rather than being kept — a column of whatever other people's markup happened to say is a
    /// column nothing can be built on.
    /// </remarks>
    public static readonly FrozenDictionary<string, ControlKind> Controls =
        new Dictionary<string, ControlKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = ControlKind.Link,
            ["link"] = ControlKind.Link,
            ["button"] = ControlKind.Button,
            ["summary"] = ControlKind.Button,
            ["tab"] = ControlKind.Button,
            ["menuitem"] = ControlKind.Button,
            ["input"] = ControlKind.Field,
            ["select"] = ControlKind.Field,
            ["textarea"] = ControlKind.Field,
            ["checkbox"] = ControlKind.Field,
            ["radio"] = ControlKind.Field,
            ["switch"] = ControlKind.Field,
            ["textbox"] = ControlKind.Field,
            ["searchbox"] = ControlKind.Field,
            ["combobox"] = ControlKind.Field,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>What sort of place a reported word describes.</summary>
    public static readonly FrozenDictionary<string, TargetKind> Targets =
        new Dictionary<string, TargetKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["internal"] = TargetKind.Internal,
            ["external"] = TargetKind.External,
            ["contact"] = TargetKind.Contact,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves what sort of control was operated.
    /// </summary>
    /// <param name="declared">The word the page used, if it used one.</param>
    /// <returns>The kind, or <see cref="ControlKind.Unknown"/> for anything not recognised.</returns>
    public static ControlKind ResolveControl(string? declared) =>
        declared is not null && Controls.TryGetValue(declared, out var control)
            ? control
            : ControlKind.Unknown;

    /// <summary>
    /// Resolves what sort of place a control pointed at.
    /// </summary>
    /// <param name="declared">The word the tracker used, if it used one.</param>
    /// <returns>The kind, or <see cref="TargetKind.None"/> where it pointed nowhere.</returns>
    public static TargetKind ResolveTarget(string? declared) =>
        declared is not null && Targets.TryGetValue(declared, out var target)
            ? target
            : TargetKind.None;

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
