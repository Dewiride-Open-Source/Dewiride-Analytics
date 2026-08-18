using System.Collections.Immutable;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;

/// <summary>
/// Splits a migration script into the individual statements it contains.
/// </summary>
/// <remarks>
/// ClickHouse's HTTP interface executes one statement per request, so a script holding several
/// statements has to be taken apart before it can be applied. Splitting on every semicolon would
/// be wrong: semicolons appear inside comments and string literals, and a migration cut in half
/// at the wrong character applies a fragment and then fails, leaving a schema that matches
/// neither the old version nor the new one. The scanner therefore steps over comments and
/// quoted text rather than searching the raw text.
/// </remarks>
internal static class SqlStatementSplitter
{
    /// <summary>
    /// Returns the statements in a script, in order, with blank fragments discarded.
    /// </summary>
    /// <param name="sql">The script text.</param>
    /// <returns>The statements, without their terminating semicolons.</returns>
    public static ImmutableArray<string> Split(string sql)
    {
        var statements = ImmutableArray.CreateBuilder<string>();
        var start = 0;
        var index = 0;

        while (index < sql.Length)
        {
            var current = sql[index];

            if (current == '-' && Peek(sql, index + 1) == '-')
            {
                index = SkipLineComment(sql, index);
            }
            else if (current == '/' && Peek(sql, index + 1) == '*')
            {
                index = SkipBlockComment(sql, index);
            }
            else if (current is '\'' or '`' or '"')
            {
                index = SkipQuoted(sql, index, current);
            }
            else if (current == ';')
            {
                Append(statements, sql.AsSpan(start, index - start));
                index++;
                start = index;
            }
            else
            {
                index++;
            }
        }

        Append(statements, sql.AsSpan(start));

        return statements.ToImmutable();
    }

    private static void Append(ImmutableArray<string>.Builder statements, ReadOnlySpan<char> statement)
    {
        var trimmed = statement.Trim();

        if (!trimmed.IsEmpty)
        {
            statements.Add(trimmed.ToString());
        }
    }

    private static char Peek(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static int SkipLineComment(string sql, int index)
    {
        var end = sql.IndexOf('\n', index);

        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int index)
    {
        var end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);

        return end < 0 ? sql.Length : end + 2;
    }

    private static int SkipQuoted(string sql, int index, char quote)
    {
        var position = index + 1;

        while (position < sql.Length)
        {
            if (sql[position] == '\\')
            {
                position += 2;
            }
            else if (sql[position] != quote)
            {
                position++;
            }
            else if (Peek(sql, position + 1) == quote)
            {
                // A doubled quote is an escaped quote, not the end of the literal.
                position += 2;
            }
            else
            {
                return position + 1;
            }
        }

        return sql.Length;
    }
}
