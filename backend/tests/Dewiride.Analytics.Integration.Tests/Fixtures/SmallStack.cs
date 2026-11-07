using System.Runtime.ExceptionServices;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Runs something on a thread with a small fraction of the stack a thread usually gets.
/// </summary>
/// <remarks>
/// A stack overflow can not be caught, so a test using this fails by ending the run. The small stack is what
/// makes the margin mean something: whatever runs here has to have stopped going deeper a long way down from
/// what would be fatal on the stacks the engine runs on.
/// </remarks>
internal static class SmallStack
{
    private const int Bytes = 256 * 1024;

    /// <summary>
    /// Runs something and hands back what it comes to, or what it threw.
    /// </summary>
    /// <typeparam name="T">What it comes to.</typeparam>
    /// <param name="work">What to run.</param>
    /// <returns>What it came to.</returns>
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception thrown)
                {
                    failure = ExceptionDispatchInfo.Capture(thrown);
                }
            },
            Bytes);

        thread.Start();
        thread.Join();
        failure?.Throw();

        return result;
    }
}
