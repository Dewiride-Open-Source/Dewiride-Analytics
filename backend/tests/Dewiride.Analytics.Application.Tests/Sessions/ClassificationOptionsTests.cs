using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Covers what an operator may set the engine's judging to.
/// </summary>
public sealed class ClassificationOptionsTests
{
    /// <summary>
    /// A visit is judged once it has been quiet for the idle timeout, so the timeout can be no
    /// shorter than the time it takes everything that decides whose a report was to arrive. The
    /// bound is a literal, as an attribute's has to be, so this is what keeps it the same figure.
    /// </summary>
    [Fact]
    public void The_Shortest_Idle_Timeout_Accepted_Is_How_Long_It_Takes_A_Report_To_Be_Settled()
    {
        var range = typeof(ClassificationOptions)
            .GetProperty(nameof(ClassificationOptions.IdleTimeout))!
            .GetCustomAttribute<RangeAttribute>()!;

        TimeSpan.Parse((string)range.Minimum, CultureInfo.InvariantCulture).Should().Be(EchoedDeliveries.Settled);
    }

    [Fact]
    public void The_Default_Idle_Timeout_Is_Accepted()
    {
        IsAccepted(new ClassificationOptions()).Should().BeTrue();
    }

    [Fact]
    public void An_Idle_Timeout_Shorter_Than_That_Is_Refused()
    {
        var options = new ClassificationOptions { IdleTimeout = EchoedDeliveries.Settled - TimeSpan.FromSeconds(1) };

        IsAccepted(options).Should().BeFalse();
    }

    private static bool IsAccepted(ClassificationOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
