using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Approves the statement that groups stored activity into visits.
/// </summary>
/// <remarks>
/// This statement decides what counts as one visit, and every verdict in the product rests on
/// that. Approving it beside the test is what makes a change to it something a person read and
/// agreed to rather than something that quietly re-cut a customer's history.
/// </remarks>
public sealed class SessionSqlCompilerTests
{
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset From = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 5, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task Reconstructing_Visits()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        return Verify(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// The activity being grouped was written by whoever visited the site, and the window comes
    /// from the engine's own bookmark. Neither is text in the statement.
    /// </summary>
    [Fact]
    public void No_Value_Appears_In_The_Statement_Text()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        statement.Sql.Should().NotContain(SiteId.ToString());
        statement.Sql.Should().NotContain(From.ToUnixTimeMilliseconds().ToString(null as IFormatProvider));
    }

    [Fact]
    public void Every_Placeholder_Has_A_Bound_Value()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        foreach (var parameter in statement.Parameters)
        {
            statement.Sql.Should().Contain($"{{{parameter.Name}:");
        }
    }

    /// <summary>
    /// Activity carrying no visitor key takes no part. A report without one has not told us an
    /// anonymous visitor was there; it has told us nothing about who was there, and gathering all
    /// of those under one empty key would build a single impossibly busy visitor and judge it.
    /// </summary>
    [Fact]
    public void Activity_That_Could_Not_Be_Attributed_Is_Left_Out()
    {
        SessionSqlCompiler.Compile(Window()).Sql.Should().Contain("visitor_key != ''");
    }

    [Fact]
    public void Compiling_Refuses_A_Missing_Window()
    {
        var act = () => SessionSqlCompiler.Compile(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static SessionWindow Window() => new()
    {
        SiteId = SiteId,
        From = From,
        To = To,
        IdleTimeout = TimeSpan.FromMinutes(30),
        MaxRequestsPerSession = 1000,
    };
}
