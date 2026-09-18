using System.Text;
using Xunit.Sdk;

namespace Dewiride.Analytics.Testing;

/// <summary>
/// What comparing a rendering with its approved file does, in each state a test can meet.
/// </summary>
/// <remarks>
/// Every test here that reads an approved file is named after it, because that is the rule under
/// test: the file is found from the test, never handed to it. The tests that fail on purpose
/// remove the received file they cause, so a green run leaves the working tree as it found it.
/// </remarks>
public sealed class SnapshotTests
{
    private const string Report =
        "--- statement ---\nSELECT 1\n\n--- bound values ---\nsite_id = 0197c0de-0000-7000-8000-000000000001\n";

    private static readonly byte[] Signature = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void Text_that_matches_passes()
    {
        var act = () => Snapshot.Matches(Report);

        act.Should().NotThrow();
    }

    [Fact]
    public void Line_endings_are_not_compared()
    {
        var windows = () => Snapshot.Matches(Report.Replace("\n", "\r\n", StringComparison.Ordinal));
        var classicMac = () => Snapshot.Matches(Report.Replace('\n', '\r'));

        windows.Should().NotThrow();
        classicMac.Should().NotThrow();
    }

    [Fact]
    public void A_difference_is_written_beside_the_approved_file_and_fails()
    {
        var (approved, received) = Paths();

        try
        {
            var act = () => Snapshot.Matches("first line\r\nthird line\r\n");

            act.Should().Throw<XunitException>()
                .WithMessage("*differs from what was approved*")
                .WithMessage($"*{received}*")
                .WithMessage($"*{approved}*")
                .WithMessage("*First difference at line 2:*approved: second line*received: third line*")
                .WithMessage("*Received:\nfirst line\nthird line\n");

            // Written the way the approved files are kept, so moving it over the approved one
            // changes nothing but the text.
            var expected = Signature.Concat(Encoding.UTF8.GetBytes("first line\nthird line\n"));
            File.ReadAllBytes(received).Should().Equal(expected);
        }
        finally
        {
            File.Delete(received);
        }
    }

    [Fact]
    public void Text_with_nothing_approved_is_written_and_fails()
    {
        var (approved, received) = Paths();

        try
        {
            var act = () => Snapshot.Matches("a rendering nobody has read yet\n");

            act.Should().Throw<XunitException>()
                .WithMessage("*Nothing has been approved*")
                .WithMessage($"*{received}*")
                .WithMessage($"*{approved}*")
                .WithMessage("*Received:\na rendering nobody has read yet\n");

            File.Exists(approved).Should().BeFalse();
            File.ReadAllText(received).Should().Be("a rendering nobody has read yet\n");
        }
        finally
        {
            File.Delete(received);
        }
    }

    [Fact]
    public void A_missing_final_line_is_a_difference()
    {
        var (_, received) = Paths();

        try
        {
            var act = () => Snapshot.Matches("a line, and a blank one after it\n");

            act.Should().Throw<XunitException>()
                .WithMessage("*differs from what was approved*")
                .WithMessage("*First difference at line 3:*received: (ends here)*");
        }
        finally
        {
            File.Delete(received);
        }
    }

    [Fact]
    public void Trailing_spaces_are_compared()
    {
        var (_, received) = Paths();

        try
        {
            var exact = () => Snapshot.Matches("trailing spaces count   \n");
            var trimmed = () => Snapshot.Matches("trailing spaces count\n");

            exact.Should().NotThrow();
            trimmed.Should().Throw<XunitException>().WithMessage("*First difference at line 1:*");
        }
        finally
        {
            File.Delete(received);
        }
    }

    [Fact]
    public void A_pass_removes_what_an_earlier_failure_wrote()
    {
        var (_, received) = Paths();
        File.WriteAllText(received, "what an earlier run rendered\n");

        var act = () => Snapshot.Matches("the approved text\n");

        act.Should().NotThrow();
        File.Exists(received).Should().BeFalse();
    }

    [Theory]
    [InlineData("one row of data")]
    public void A_test_that_takes_arguments_is_refused(string rendered)
    {
        var (approved, received) = Paths();

        try
        {
            var act = () => Snapshot.Matches(rendered);

            act.Should().Throw<InvalidOperationException>().WithMessage("*takes arguments*");
            File.Exists(approved).Should().BeFalse();
            File.Exists(received).Should().BeFalse();
        }
        finally
        {
            File.Delete(received);
        }
    }

    /// <summary>
    /// A thread started with the execution context held back carries none of the test's own
    /// context, which is what code running outside a test sees.
    /// </summary>
    [Fact]
    public void A_comparison_outside_a_test_is_refused()
    {
        Exception? caught = null;

        using (ExecutionContext.SuppressFlow())
        {
            var thread = new Thread(() => caught = Record.Exception(() => Snapshot.Matches("anything")));
            thread.Start();
            thread.Join();
        }

        caught.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("inside a test");
    }

    [Fact]
    public void An_assembly_without_a_recorded_directory_is_refused()
    {
        var act = () => Snapshot.ProjectDirectory(typeof(object).Assembly);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not record the directory*");
    }

    [Fact]
    public void A_null_rendering_is_refused()
    {
        var act = () => Snapshot.Matches(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The approved and received paths of the calling test, derived the way the comparison derives them.
    /// </summary>
    private static (string Approved, string Received) Paths()
    {
        var test = TestContext.Current.TestCase!;
        var stem = Path.Combine(
            Snapshot.ProjectDirectory(typeof(SnapshotTests).Assembly),
            $"{test.TestClassSimpleName}.{test.TestMethodName}");

        return ($"{stem}.verified.txt", $"{stem}.received.txt");
    }
}
