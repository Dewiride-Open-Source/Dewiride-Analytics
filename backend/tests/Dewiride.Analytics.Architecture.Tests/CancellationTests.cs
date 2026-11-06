using System.Text.RegularExpressions;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that no failure is taken for a cancellation because of its type.
/// </summary>
/// <remarks>
/// An exception that leaves a background service stops the host, and with it the collector. A
/// <c>catch</c> that keeps a loop alive and lets every cancellation through by its type lets one
/// through that nobody asked for: a client that stops waiting for a statement reports it as a
/// cancellation. Whether work was cancelled is a question about the token the work was given, so a
/// filter asks the token.
/// </remarks>
[Trait("Category", "Cancellation")]
public sealed partial class CancellationTests
{
    /// <summary>
    /// No source file tells a failure worth handling from a cancellation by what the exception is.
    /// </summary>
    [Fact]
    public void No_Failure_Is_Let_Through_For_Being_A_Cancellation()
    {
        var offenders = Product.SourceFiles()
            .Where(file => File.ReadLines(file.FullName).Any(line => ToldApartByType().IsMatch(line)))
            .Select(file => file.FullName);

        offenders.Should().BeEmpty();
    }

    [GeneratedRegex(@"\bis\s+not\s+(OperationCanceledException|TaskCanceledException)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ToldApartByType();
}
