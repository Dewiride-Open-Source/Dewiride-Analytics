using System.Globalization;
using System.Text.Json;
using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Api.Tests.Configuration;

/// <summary>
/// Holds what the dashboard says about closing an account to what the engine actually does.
/// </summary>
/// <remarks>
/// <para>
/// The card that closes an account tells the owner how long they have to change their mind. The
/// number is written into the sentence, as every figure in the catalogue is, because a sentence
/// assembled around a value at run time is not one a translator can rewrite. This is what stops
/// the sentence and <see cref="Organization.ClosureRetention"/> drifting apart: a promise about
/// how long somebody's data is kept, made by a product whose whole proposition is that what it
/// says can be relied on.
/// </para>
/// <para>
/// The dashboard's catalogue is linked into this project's output rather than transcribed, so
/// this reads the same text the dashboard is built from.
/// </para>
/// </remarks>
public sealed class ClosureCopyTests
{
    [Fact]
    public void The_Card_That_Closes_An_Account_Promises_The_Retention_The_Engine_Keeps()
    {
        var days = Organization.ClosureRetention.TotalDays.ToString(CultureInfo.InvariantCulture);

        CloseCardBody().Should().Contain(
            $"{days} days",
            "the card promises the number of days the engine keeps a closed account for");
    }

    /// <summary>The sentence on the card, read from the dashboard's own catalogue.</summary>
    private static string CloseCardBody()
    {
        using var catalogue = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "en.json")));

        return catalogue.RootElement
            .GetProperty("settings")
            .GetProperty("close")
            .GetProperty("body")
            .GetString()
            ?? throw new InvalidOperationException("The dashboard's catalogue has no sentence on the card that closes an account.");
    }
}
