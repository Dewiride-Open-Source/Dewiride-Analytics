using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;

/// <summary>
/// Reads the migration scripts embedded in this assembly.
/// </summary>
/// <remarks>
/// Scripts are named <c>NNNN_description.sql</c> and are applied in ascending numeric order. The
/// number is the migration's identity for the rest of its life: it is what gets recorded as
/// applied, so renaming a script after it has shipped makes a self-hoster's database look as
/// though the migration were missing.
/// </remarks>
internal static class MigrationScriptCatalog
{
    private const string ScriptExtension = ".sql";
    private const char VersionSeparator = '_';

    /// <summary>
    /// Loads every embedded script, ordered by version.
    /// </summary>
    /// <returns>The scripts.</returns>
    /// <exception cref="InvalidOperationException">
    /// A script does not follow the naming convention, or two scripts share a version.
    /// </exception>
    public static ImmutableArray<MigrationScript> Load()
    {
        var assembly = typeof(MigrationScriptCatalog).Assembly;

        var scripts = assembly.GetManifestResourceNames()
            .Where(resource => resource.EndsWith(ScriptExtension, StringComparison.Ordinal))
            .Select(resource => Read(assembly, resource))
            .OrderBy(script => script.Version)
            .ToImmutableArray();

        var duplicate = scripts.GroupBy(script => script.Version).FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two ClickHouse migrations share version {duplicate.Key}: "
                + string.Join(", ", duplicate.Select(script => script.Name))
                + ". Versions must be unique.");
        }

        return scripts;
    }

    private static MigrationScript Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' could not be opened.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Line endings are normalised before hashing so that a repository checked out with
        // native endings produces the same checksum on every platform.
        var sql = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var (version, name) = ParseFileName(resourceName);

        return new MigrationScript(version, name, Checksum(sql), SqlStatementSplitter.Split(sql));
    }

    private static (uint Version, string Name) ParseFileName(string resourceName)
    {
        var fileName = resourceName[..^ScriptExtension.Length];
        var lastDot = fileName.LastIndexOf('.');

        if (lastDot >= 0)
        {
            fileName = fileName[(lastDot + 1)..];
        }

        var separator = fileName.IndexOf(VersionSeparator, StringComparison.Ordinal);

        if (separator <= 0
            || !uint.TryParse(fileName[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException(
                $"ClickHouse migration '{fileName}{ScriptExtension}' is not named NNNN_description{ScriptExtension}.");
        }

        return (version, fileName[(separator + 1)..]);
    }

    private static string Checksum(string sql) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
