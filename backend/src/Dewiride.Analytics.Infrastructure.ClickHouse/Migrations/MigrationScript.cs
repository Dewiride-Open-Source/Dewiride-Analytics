using System.Collections.Immutable;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;

/// <summary>
/// One migration script, ready to apply.
/// </summary>
/// <param name="Version">Ordering number taken from the file name.</param>
/// <param name="Name">Descriptive part of the file name.</param>
/// <param name="Checksum">
/// Hash of the script text with line endings normalised, so that the same script checked out on
/// Windows and on Linux produces the same value. Recorded when the migration is applied and
/// compared on every subsequent start, which is what turns an edited migration into a loud
/// failure rather than a schema that silently disagrees with the code that reads it.
/// </param>
/// <param name="Statements">The statements the script contains, in order.</param>
internal sealed record MigrationScript(
    uint Version,
    string Name,
    string Checksum,
    ImmutableArray<string> Statements);
