using MaxMind.Db;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// The part of a place record this product reads.
/// </summary>
/// <remarks>
/// <para>
/// The published database holds a good deal more than this — continent, postcode, coordinates,
/// time zone, whether the country is in the European Union. Only three fields are declared, and
/// that is the point: what is not declared is not read, so a decision to collect more than the
/// question needs would have to be made here, deliberately, and would show up in a diff.
/// </para>
/// <para>
/// Coordinates are the notable omission. A country list and a town list need neither, and a pair
/// of numbers locating a visitor to within a few streets is a different kind of record from the
/// name of their nearest town.
/// </para>
/// </remarks>
internal sealed class PlaceRecord
{
    /// <summary>The country.</summary>
    [MapKey("country")]
    public NamedPlace? Country { get; init; }

    /// <summary>
    /// States, provinces or regions, largest first.
    /// </summary>
    /// <remarks>
    /// A list because some countries nest them. Only the first is kept: the outermost is the one
    /// a reader recognises, and the rest describe an administrative hierarchy nobody asked about.
    /// </remarks>
    [MapKey("subdivisions")]
    public IReadOnlyList<NamedPlace>? Subdivisions { get; init; }

    /// <summary>The town or city.</summary>
    [MapKey("city")]
    public NamedPlace? City { get; init; }
}

/// <summary>
/// A place, as the database names it.
/// </summary>
/// <remarks>
/// Names arrive in several languages in the paid databases and in English alone in the free one.
/// The country is therefore taken from its code and translated by the interface, and only the
/// town — which has no code — is displayed as the database wrote it.
/// </remarks>
internal sealed class NamedPlace
{
    /// <summary>Standard code for the place, where it has one.</summary>
    [MapKey("iso_code")]
    public string? IsoCode { get; init; }

    /// <summary>The place's name, by language.</summary>
    [MapKey("names")]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    /// <summary>The English name, or empty when the record carries none.</summary>
    public string EnglishName =>
        Names is not null && Names.TryGetValue("en", out var name) ? name : string.Empty;
}
