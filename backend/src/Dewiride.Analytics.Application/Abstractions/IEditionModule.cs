using Microsoft.Extensions.Hosting;

namespace Dewiride.Analytics.Application.Abstractions;

/// <summary>
/// Registers the services that differ between the open-source and commercial editions.
/// </summary>
/// <remarks>
/// <para>
/// This is the single seam on which the two editions diverge. Exactly one implementation is
/// compiled into any given build: <c>CommunityEditionModule</c> ships in <c>backend/src</c>
/// and <c>CloudEditionModule</c> only in <c>ee/</c>, whose project references are conditioned
/// on the <c>DewirideEdition</c> MSBuild property. Which edition you get is therefore decided
/// by which projects were compiled.
/// </para>
/// <para>
/// This is a composition root rather than conditional compilation because Roslyn analyzers
/// and SonarQube do not analyse an inactive <c>#if</c> branch: under <c>#if EE</c> half the
/// codebase would sit outside a quality gate this repository treats as non-negotiable, and
/// coverage across the two editions would stop meaning anything.
/// See <c>docs/adr/0002-single-public-repo-with-ee.md</c>.
/// </para>
/// </remarks>
public interface IEditionModule
{
    /// <summary>
    /// Name of the edition, for logging and for the build-information endpoint.
    /// </summary>
    string EditionName { get; }

    /// <summary>
    /// Registers the edition's services.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    void Register(IHostApplicationBuilder builder);
}
