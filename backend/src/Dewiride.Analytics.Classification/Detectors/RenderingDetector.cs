using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports how much of the page the visitor actually asked for.
/// </summary>
/// <remarks>
/// <para>
/// The difference between reading a page and taking its text. A browser fetches the markup and
/// then everything the markup refers to — scripts, images, stylesheets. A crawler asks for the
/// markup and stops.
/// </para>
/// <para>
/// This only means anything when something in the request path saw the visit, because that is
/// the only way to know a session existed at all when no script ran. Without it, a session that
/// executed nothing is simply a session nothing reported, and the correct conclusion is silence.
/// </para>
/// </remarks>
public sealed class RenderingDetector : IDetector
{
    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.ScriptRan)
        {
            // Moderate rather than decisive. Software that drives a real browser executes
            // everything a person's browser does, so this rules out the crudest automation and
            // nothing more.
            return [Observed.Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35)];
        }

        if (!session.ServerObserved)
        {
            return [];
        }

        return session.ImagesFetched

            // Something rendered the markup far enough to fetch what it referred to, which a
            // crawler taking text does not do. Weak, because a person with scripting switched off
            // and a well-built crawler are hard to tell apart on this alone.
            ? [Observed.Signal(SignalCodes.ImagesOnly, SignalDirection.TowardHuman, 25)]
            : [Observed.Signal(SignalCodes.NoScriptExecution, SignalDirection.TowardAutomation, 50)];
    }
}

/// <summary>
/// Reports the things a browser always says and other software usually forgets.
/// </summary>
public sealed class ClientDeclarationDetector : IDetector
{
    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        if (session.DeclaredWebDriver == true)
        {
            // The browser volunteered it. Trivially switched off by anybody who would rather it
            // were not there, which is why this is strong when present and means nothing at all
            // when absent.
            found.Add(Observed.Signal(SignalCodes.DeclaredWebDriver, SignalDirection.TowardAutomation, 75));
        }

        if (string.IsNullOrWhiteSpace(session.Language))
        {
            // Every browser asks for a language, because every person has one. A fetching library
            // has nothing to ask for and says nothing.
            found.Add(Observed.Signal(SignalCodes.NoLanguageDeclared, SignalDirection.TowardAutomation, 25));
        }

        return found.ToImmutable();
    }
}
