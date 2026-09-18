using System.Reflection;
using System.Text;
using Xunit.Sdk;
using Xunit.v3;

namespace Dewiride.Analytics.Testing;

/// <summary>
/// Compares what a test rendered with the text approved for it.
/// </summary>
/// <remarks>
/// <para>
/// The approved text is a file in the test project's directory named after the test,
/// <c>{Class}.{Method}.verified.txt</c>, committed and reviewed like any other source. A run that
/// renders something else writes what it saw to <c>{Class}.{Method}.received.txt</c> beside it
/// and fails naming both files, and approving the change is reading the received file and moving
/// it over the approved one. A run that matches removes any received file an earlier one left, so
/// a working tree is clean again the moment a change is approved.
/// </para>
/// <para>
/// The failure carries the rendering in its message as well, with the first line that differs,
/// because on a build agent the received file is discarded with the machine and the log is all
/// that reaches a reader.
/// </para>
/// <para>
/// Line endings are not part of what is approved. The approved file is written with line feeds,
/// the renderers write whatever the platform uses, and source control on a contributor's machine
/// may rewrite either, so both sides are read as line feeds before they are compared. Everything
/// else is compared exactly, trailing newlines and trailing spaces included: a renderer that stops
/// ending its output the way it did is a change somebody should read.
/// </para>
/// </remarks>
public static class Snapshot
{
    /// <summary>
    /// The assembly metadata every test project carries, naming the directory it was built from.
    /// </summary>
    /// <remarks>
    /// Recorded at build time because nothing at run time says where a test project is: on a build
    /// agent the compiler maps every source path to a placeholder, and the output directory is
    /// wherever the build was told to put it.
    /// </remarks>
    internal const string ProjectDirectoryKey = "Dewiride.Analytics.Testing.ProjectDirectory";

    private const string ApprovedSuffix = ".verified.txt";
    private const string ReceivedSuffix = ".received.txt";

    /// <summary>
    /// The encoding the approved files are kept in, so a received file moved over one keeps it.
    /// </summary>
    private static readonly UTF8Encoding Utf8WithSignature = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Passes when <paramref name="rendered"/> is the text approved for the calling test, and
    /// otherwise writes it beside the approved file and fails.
    /// </summary>
    /// <param name="rendered">What the test produced.</param>
    /// <exception cref="InvalidOperationException">
    /// Called from outside a test, or from a test that takes arguments: such a test runs once per
    /// row of its data, and every row would be compared against the one file.
    /// </exception>
    /// <exception cref="XunitException">
    /// The rendering differs from the approved text, or nothing has been approved yet.
    /// </exception>
    public static void Matches(string rendered)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        var stem = Locate(TestContext.Current);
        var approvedPath = stem + ApprovedSuffix;
        var receivedPath = stem + ReceivedSuffix;

        var received = Normalise(rendered);
        var approved = File.Exists(approvedPath) ? Normalise(File.ReadAllText(approvedPath)) : null;

        if (string.Equals(approved, received, StringComparison.Ordinal))
        {
            File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, received, Utf8WithSignature);

        throw new XunitException(Explain(approved, received, approvedPath, receivedPath));
    }

    /// <summary>
    /// The directory a test assembly was built from, which is where its approved files are.
    /// </summary>
    /// <param name="assembly">A test assembly.</param>
    /// <returns>The absolute path of the project directory.</returns>
    /// <exception cref="InvalidOperationException">
    /// The assembly does not record one, or records one that is not on this machine.
    /// </exception>
    internal static string ProjectDirectory(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var directory = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == ProjectDirectoryKey)
            ?.Value
            ?? throw new InvalidOperationException(
                $"{assembly.GetName().Name} does not record the directory it was built from, so nothing "
                + "says where its approved files are. Every project under backend/tests records it "
                + "through the shared Directory.Build.props; a test assembly built from anywhere else "
                + "has to declare the same assembly metadata itself.");

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} records {directory} as the directory it was built from, and "
                + "that directory is not on this machine. A snapshot is compared from the checkout the "
                + "suite was built in, never from a copied output.");
        }

        return directory;
    }

    /// <summary>
    /// The path, without its suffix, of the files belonging to the running test.
    /// </summary>
    private static string Locate(ITestContext context)
    {
        if (context.TestCase is not { TestClassSimpleName: { } className, TestMethodName: { } methodName }
            || context.TestClass is not ICoreTestClass { Class: var testClass })
        {
            throw new InvalidOperationException(
                "A snapshot is named after the test comparing it, so it can only be compared from inside a test.");
        }

        if (context.TestMethod is IXunitTestMethod { Parameters.Count: > 0 })
        {
            throw new InvalidOperationException(
                $"{className}.{methodName} takes arguments, so it runs once per row of its data and every "
                + "row would be compared against the one approved file. A snapshot belongs to a test "
                + "without arguments.");
        }

        return Path.Combine(ProjectDirectory(testClass.Assembly), $"{className}.{methodName}");
    }

    /// <summary>
    /// The text with every line ending as a line feed, which is how the approved files are written.
    /// </summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>
    /// What a reader needs when the two texts differ: where the files are, what to do, the first
    /// line that differs, and the whole of what was rendered.
    /// </summary>
    private static string Explain(string? approved, string received, string approvedPath, string receivedPath)
    {
        var message = new StringBuilder();

        if (approved is null)
        {
            message.Append("Nothing has been approved for this test yet. What it rendered is at ")
                .Append(receivedPath)
                .Append("; read it, and move it to ")
                .Append(approvedPath)
                .AppendLine(" to approve it.");
        }
        else
        {
            message.Append("What this test rendered differs from what was approved. The rendering is at ")
                .Append(receivedPath)
                .Append("; read it against ")
                .Append(approvedPath)
                .AppendLine(", and move the received file over the approved one to approve the change.");
            AppendFirstDifference(message, approved, received);
        }

        message.AppendLine().AppendLine("Received:").Append(received);

        return message.ToString();
    }

    /// <summary>
    /// The first line at which the two texts part, or the point at which one of them ends.
    /// </summary>
    private static void AppendFirstDifference(StringBuilder message, string approved, string received)
    {
        var approvedLines = approved.Split('\n');
        var receivedLines = received.Split('\n');
        var shared = Math.Min(approvedLines.Length, receivedLines.Length);

        var line = 0;
        while (line < shared && string.Equals(approvedLines[line], receivedLines[line], StringComparison.Ordinal))
        {
            line++;
        }

        message.AppendLine().Append("First difference at line ").Append(line + 1).AppendLine(":");
        message.Append("  approved: ").AppendLine(line < approvedLines.Length ? approvedLines[line] : "(ends here)");
        message.Append("  received: ").AppendLine(line < receivedLines.Length ? receivedLines[line] : "(ends here)");
    }
}
