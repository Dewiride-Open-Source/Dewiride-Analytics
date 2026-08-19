using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Reads the specific reasons a refusal named.
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

    private sealed record RefusalDocument(IReadOnlyList<RefusedReason>? Problems);
}
