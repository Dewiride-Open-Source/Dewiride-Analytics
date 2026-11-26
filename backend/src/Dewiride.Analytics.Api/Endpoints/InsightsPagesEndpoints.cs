using System.Globalization;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Dewiride.Analytics.Api.Analytics.QueryVocabulary;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading every page of a website a page at a time, each with its views across the website's whole
/// record, the most viewed first and searchable by address.
/// </summary>
/// <remarks>
/// <para>
/// Beside the readings of a website's posts, and on their terms: a page's views are read from what was kept
/// of each month and, from the first month nothing was kept of the pages of, from the reports, by the
/// arithmetic of the dashboard's list of pages. A month keeps only its busiest pages, and the answer says
/// where one it is read from left any out. Every page is listed whether or not the website has a sitemap,
/// its posts among them, each at its own path.
/// </para>
/// <para>
/// On the same terms as every other read of a website's numbers: everything the caller supplied is checked
/// before the website is resolved, and a website that does not exist and one the caller has no role on are
/// answered identically. A path is whatever was asked for: it travels in the answer as a value and is never
/// logged. A search is the caller's own text: it is never logged and no refusal repeats it, and it reaches the
/// telemetry store only as words bound to the statement, never as part of it.
/// </para>
/// </remarks>
internal static class InsightsPagesEndpoints
{
    /// <summary>How a month is written in an answer.</summary>
    private const string MonthFormat = "yyyy-MM";

    /// <summary>Pages on a page when the caller does not say how many: a page somebody reads down.</summary>
    private const int DefaultPages = 25;

    /// <summary>What a page of pages allows.</summary>
    private static readonly ListBounds PagesBounds = new(DefaultPages, AllPagesQuery.MostPages, "pages");

    /// <summary>
    /// Maps the question about every page of a website.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapInsightsPages(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/insights/pages", PagesAsync)
            .WithName("SiteAllPages")
            .WithSummary("Returns one page of every page of a website with its views across its whole record, the most viewed first and searchable by address.");
    }

    /// <summary>
    /// Answers with one page of every page of a website.
    /// </summary>
    /// <remarks>
    /// Every month instant comes from the store, through the record the other readings of it read, so the
    /// stretch counted from the reports begins where a month of the record begins. A website nothing is held
    /// of is answered at once, with no page.
    /// </remarks>
    private static async Task<Results<Ok<AllPagesResponse>, NotFound, ProblemHttpResult>> PagesAsync(
        [AsParameters] AllPagesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryReadAsked(parameters, out var asked, out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var asOf = clock.GetUtcNow();
        var record = await telemetry
            .GetKeptMonthsAsync(scope, new KeptMonthsQuery(asOf), cancellationToken)
            .ConfigureAwait(false);
        var pageMonths = await telemetry
            .GetKeptPageMonthsAsync(scope, new KeptPageMonthsQuery(asOf), cancellationToken)
            .ConfigureAwait(false);
        DateOnly[] kept = [.. pageMonths.Select(month => month.Month)];

        if (PostsInsights.LiveFrom(record.Months, kept, asOf) is not { } liveFrom)
        {
            return TypedResults.Ok(new AllPagesResponse(0, null, false, 0, []));
        }

        var slice = await telemetry
            .GetAllPagesAsync(
                scope,
                new AllPagesQuery(asOf, liveFrom, PostSearch.WordsOf(asked.Search), asked.Paging.Limit, asked.Paging.Offset)
                {
                    Population = asked.Population,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new AllPagesResponse(
                slice.Matching,
                slice.Matching == 0
                    ? null
                    : PostsInsights.ViewsFrom(kept, liveFrom)?.ToString(MonthFormat, CultureInfo.InvariantCulture),
                pageMonths.Any(month => month.Month < liveFrom.Month && month.LeftOutFor(asked.Population)),
                slice.MostPageViews,
                [.. slice.Shown.Select(page => new ListedPageRow(page.Path, page.PageViews))]));
    }

    /// <summary>
    /// Checks everything a question about a page of pages asked, in the order its refusal names the first
    /// thing wrong with it: how much of the list and from where, the search, and who it is about.
    /// </summary>
    /// <param name="parameters">What the caller supplied.</param>
    /// <param name="asked">The checked question, where it can be answered as asked.</param>
    /// <param name="refusal">Why it cannot, where it cannot.</param>
    /// <returns><see langword="true"/> when the question can be answered as asked.</returns>
    private static bool TryReadAsked(AllPagesParameters parameters, out PagesAsked asked, out string? refusal)
    {
        asked = default;

        if (!TryReadPaging(parameters.Limit, parameters.Offset, PagesBounds, out var paging, out refusal))
        {
            return false;
        }

        if (!PostSearch.TryRead(parameters.Search, out var search))
        {
            refusal = SearchTooLong;

            return false;
        }

        if (!TryReadPopulation(parameters.Only, out var population))
        {
            refusal = UnknownPopulation;

            return false;
        }

        asked = new PagesAsked(paging, search, population);

        return true;
    }

    /// <summary>What a question about a page of pages asked, once checked.</summary>
    /// <param name="Paging">How many pages the page holds, and how many come before it.</param>
    /// <param name="Search">What a page's address must hold, or nothing where nothing was searched for.</param>
    /// <param name="Population">Who the pages' views are counted for.</param>
    private readonly record struct PagesAsked(Paging Paging, string? Search, Population Population);
}

/// <summary>
/// What the question about a page of every page of a website reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Search">
/// The words a page's address must hold, every one of them and without regard to case, or absent for every
/// page. Written by whoever asks, so it is only ever a value: never logged and never repeated.
/// </param>
/// <param name="Limit">How many pages the page holds, from 1 to 100. Defaults to 25.</param>
/// <param name="Offset">How many of the most viewed pages come before the page. Defaults to none.</param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or absent for
/// everybody.
/// </param>
internal readonly record struct AllPagesParameters(
    Guid SiteId,
    [FromQuery] string? Search,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset,
    [FromQuery] string? Only);
