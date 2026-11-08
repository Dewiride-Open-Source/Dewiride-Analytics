using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dewiride.Analytics.Api.Contracts;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Reads what a refusal said: the specific reasons it named, or the whole of it.
/// </summary>
/// <remarks>
/// A test asserts the code rather than the status, because the code is what the dashboard looks
/// its own sentence up by. Two refusals that share a status and differ only in what the reader is
/// told would be indistinguishable to a test that read the status alone, and swapping one for the
/// other is exactly the kind of change nobody notices until a customer is shown the wrong advice.
/// </remarks>
internal static class Refusal
{
    /// <summary>
    /// The codes a refusal named.
    /// </summary>
    /// <param name="response">The answer to read.</param>
    /// <returns>The codes, which may be none.</returns>
    public static async Task<IReadOnlyList<string>> ReasonsOfAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var refused = await response.Content
            .ReadFromJsonAsync<RefusalDocument>(Cancellation.Token)
            .ConfigureAwait(false);

        return [.. (refused?.Problems ?? []).Select(problem => problem.Code)];
    }

    /// <summary>
    /// Everything a refusal says, apart from the identifier that ties it to a line in the log, and
    /// whether it repeats what was asked.
    /// </summary>
    /// <remarks>
    /// That identifier is different on every request by design and tells a caller nothing. The rest
    /// of the problem is kept whole, so a member one refusal carries and another does not is a
    /// difference between them.
    /// </remarks>
    /// <param name="response">The answer to read.</param>
    /// <param name="asked">What the caller wrote that no refusal may repeat.</param>
    /// <returns>What the refusal said.</returns>
    public static async Task<Said> WholeAsync(HttpResponseMessage response, string asked)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(asked);

        var body = await response.Content.ReadAsStringAsync(Cancellation.Token).ConfigureAwait(false);
        var problem = JsonNode.Parse(body)!.AsObject();

        problem.Remove("traceId");

        return new Said(response.StatusCode, problem.ToJsonString(), body.Contains(asked, StringComparison.Ordinal));
    }

    /// <summary>What a refusal said.</summary>
    /// <param name="Status">The status it was answered with.</param>
    /// <param name="Problem">The problem it carried, whole but for the identifier that ties it to a line in the log.</param>
    /// <param name="RepeatsWhatWasAsked">Whether anything in it repeats what was asked.</param>
    public sealed record Said(HttpStatusCode Status, string Problem, bool RepeatsWhatWasAsked);

    private sealed record RefusalDocument(IReadOnlyList<RefusedReason>? Problems);
}
