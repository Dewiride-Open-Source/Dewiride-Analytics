namespace Dewiride.Analytics.Api.Configuration;

/// <summary>
/// How the two stores' schemas are brought up to date.
/// </summary>
public sealed class SchemaOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Schema";

    /// <summary>
    /// Whether pending migrations are applied while the process starts, before it serves traffic.
    /// </summary>
    /// <remarks>
    /// On by default, because the product promises that bringing the stack up is one command and
    /// a self-hoster has no release pipeline to run migrations from. Turn it off where schema
    /// changes are applied by a separate, supervised step — for instance a deployment that runs
    /// several instances, where they would otherwise all migrate at once.
    /// </remarks>
    public bool ApplyOnStartup { get; init; } = true;
}
