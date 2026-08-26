using System.Text.RegularExpressions;
using Dewiride.Analytics.Infrastructure.Accounts;

namespace Dewiride.Analytics.Api.Tests.Configuration;

/// <summary>
/// That every screen the engine sends somebody to is one the dashboard actually serves.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of this product each keep their own list of screens, and they have to agree. The
/// dashboard's is authoritative — a screen exists because it has a file and an entry in
/// <c>SCREENS</c> — and the engine's is <see cref="DashboardScreens"/>, used to build the address
/// somebody is returned to after paying and every link this product sends by email.
/// </para>
/// <para>
/// Nothing else can catch this. A link to a screen that has moved is a well-formed address that
/// the proxy answers and that shows the reader a page saying there is nothing there; from the
/// engine's side the send succeeded and nothing is logged. Moving the plan screen under the
/// account settings left three copies of its old path behind, and they were found by a customer
/// paying for a plan and landing on a missing page.
/// </para>
/// <para>
/// The dashboard's file is linked into this project's output rather than transcribed, so this test
/// reads the same text the dashboard is built from.
/// </para>
/// </remarks>
public sealed partial class DashboardScreenTests
{
    [Fact]
    public void Every_screen_the_engine_links_to_is_one_the_dashboard_serves()
    {
        var served = Served();

        served.Should().NotBeEmpty("the dashboard is expected to declare the screens it serves");
        DashboardScreens.All.Should().NotBeEmpty();

        foreach (var screen in DashboardScreens.All)
        {
            served.Should().Contain(
                $"/{screen}",
                "the engine builds links to /{0}, so the dashboard has to answer it",
                screen);
        }
    }

    /// <summary>
    /// Every screen is named once and written the way the link builder expects.
    /// </summary>
    /// <remarks>
    /// A leading slash would make the address absolute against the installation's own, discarding
    /// any path it is published under; a duplicate is a copied line somebody forgot to finish.
    /// </remarks>
    [Fact]
    public void Every_screen_is_named_once_and_without_a_leading_slash()
    {
        DashboardScreens.All.Should().OnlyHaveUniqueItems();
        DashboardScreens.All.Should().AllSatisfy(screen =>
        {
            screen.Should().NotStartWith("/");
            screen.Should().NotBeNullOrWhiteSpace();
        });
    }

    /// <summary>
    /// The addresses the dashboard says it serves, read from its own list.
    /// </summary>
    /// <remarks>
    /// The entries in that list are names rather than addresses, so the names are resolved against
    /// the constants declared above them. Reading the set rather than every constant in the file is
    /// deliberate: a constant that is not in the set is an address the dashboard does not answer,
    /// which is exactly the case this test exists to fail on.
    /// </remarks>
    private static string[] Served()
    {
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "routes.ts"));

        var addresses = Declaration()
            .Matches(source)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);

        var listed = ScreenSet().Match(source);

        listed.Success.Should().BeTrue("the dashboard is expected to declare a set of screens");

        return [.. Name()
            .Matches(listed.Groups[1].Value)
            .Select(match => match.Value)
            .Where(addresses.ContainsKey)
            .Select(name => addresses[name])];
    }

    [GeneratedRegex(@"^export const ([A-Z_][A-Z0-9_]*) = '([^']+)';", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"export const SCREENS[^=]*= new Set\(\[(.*?)\]\);", RegexOptions.Singleline)]
    private static partial Regex ScreenSet();

    [GeneratedRegex("[A-Z_][A-Z0-9_]*")]
    private static partial Regex Name();
}
