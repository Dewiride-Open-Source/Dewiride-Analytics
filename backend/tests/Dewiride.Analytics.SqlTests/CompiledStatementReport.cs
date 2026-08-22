using System.Globalization;
using System.Text;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Renders a compiled statement as the text a reviewer approves.
/// </summary>
/// <remarks>
/// The statement and its bound values are written out plainly rather than serialised, because the
/// approved file is read by a person deciding whether a change to the generated SQL is intended.
/// A form that escapes the newlines out of a statement would defeat the only purpose it has.
/// </remarks>
internal static class CompiledStatementReport
{
    /// <summary>
    /// Renders a statement and its parameters.
    /// </summary>
    /// <param name="statement">The compiled statement.</param>
    /// <returns>The text to compare against the approved snapshot.</returns>
    public static string Render(CompiledStatement statement)
    {
        var report = new StringBuilder();

        report.AppendLine("--- statement ---");
        report.AppendLine(statement.Sql);
        report.AppendLine();
        report.AppendLine("--- bound values ---");

        foreach (var parameter in statement.Parameters)
        {
            report.Append(parameter.Name)
                .Append(" = ")
                .AppendLine(Describe(parameter.Value));
        }

        return report.ToString();
    }

    private static string Describe(object value) => value switch
    {
        Guid identifier => identifier.ToString(),
        // Written out in full. A catalogue bound as an array is a value this statement depends on
        // for its answer, and a count of its entries would let a change to what is in it pass an
        // approved file unchanged.
        string[] entries => $"[{string.Join(", ", entries.Select(entry => $"'{entry}'"))}]",
        uint[] numbers => $"[{string.Join(", ", numbers.Select(number => number.ToString(CultureInfo.InvariantCulture)))}]",
        Guid[] identifiers => $"[{string.Join(", ", identifiers)}]",
        long number => number.ToString(CultureInfo.InvariantCulture),
        string text => $"'{text}'",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
