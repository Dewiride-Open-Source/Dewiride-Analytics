using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// What one pass over one website has to go on, and what it has found out so far.
/// </summary>
/// <remarks>
/// Lasts as long as the pass and is used from one place at a time. It carries the website's robots.txt
/// for the same length of time, so the file is asked for once however many pages the pass goes on to ask
/// it about.
/// </remarks>
internal sealed class SitemapPassContext
{
    /// <summary>
    /// How many things in a row in trouble it takes to say it is the website that is in trouble, and to
    /// stop asking it for more.
    /// </summary>
    public const int MostTroubleInARow = 3;

    /// <summary>
    /// The longest a website's asking to be left alone is taken at its word. Nobody writes a longer one
    /// and means it, and a page should not be able to park itself for decades by saying so.
    /// </summary>
    private static readonly TimeSpan LongestLeftAlone = TimeSpan.FromDays(7);

    private readonly TimeProvider _clock;
    private readonly long _began;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _afterFailure;

    /// <summary>Begins a pass.</summary>
    /// <param name="lease">The claim it runs under.</param>
    /// <param name="sitemap">The address the claim is for, read.</param>
    /// <param name="row">How far reading the website had got when it was claimed.</param>
    /// <param name="site">The website.</param>
    /// <param name="fetcher">Asks a website for things.</param>
    /// <param name="settings">How long it may run for, and how long a website is left alone for.</param>
    /// <param name="clock">The clock its budget runs on.</param>
    public SitemapPassContext(
        SitemapLease lease,
        Uri sitemap,
        SitemapRead row,
        Site site,
        WebsiteFetcher fetcher,
        SitemapOptions settings,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(sitemap);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        Lease = lease;
        TimeZoneId = site.TimeZoneId;
        Sitemap = sitemap;
        Bounds = new WebsiteBounds(site, Sitemap);
        Robots = new RobotsPass(fetcher, Bounds);
        ReadingKind = row.RootKind;
        Validators = new WebsiteValidators(row.RootEtag, row.RootLastModified);
        ListProblem = row.RootProblem;
        ReadingStartedAt = row.CycleStartedAt;
        NextReadingAt = row.NextCycleAt;

        _clock = clock;
        _began = clock.GetTimestamp();
        _budget = settings.PassBudget;
        _afterFailure = settings.RetryAfterFailure;
    }

    /// <summary>The claim it runs under.</summary>
    public SitemapLease Lease { get; }

    /// <summary>The website's reporting zone.</summary>
    public string TimeZoneId { get; }

    /// <summary>The sitemap's own address.</summary>
    public Uri Sitemap { get; }

    /// <summary>Where it may go.</summary>
    public WebsiteBounds Bounds { get; }

    /// <summary>What the website's robots.txt says, as far as this pass has asked.</summary>
    public RobotsPass Robots { get; }

    /// <summary>What the address held the last time it was read.</summary>
    public SitemapRootKind? ReadingKind { get; }

    /// <summary>What to ask the address with, to be told if nothing has changed.</summary>
    public WebsiteValidators Validators { get; }

    /// <summary>What was wrong with the list of parts the address held.</summary>
    public SitemapProblem? ListProblem { get; set; }

    /// <summary>When the reading under way began, or nothing where none is.</summary>
    public DateTimeOffset? ReadingStartedAt { get; set; }

    /// <summary>When the sitemap is next read from the top.</summary>
    public DateTimeOffset NextReadingAt { get; set; }

    /// <summary>
    /// Whether an attempt at the address is in hand, and would be wasted if the engine stopped now.
    /// </summary>
    public bool AddressInHand { get; set; }

    /// <summary>The part an attempt is in hand for, and would be wasted if the engine stopped now.</summary>
    public Guid? PartInHand { get; set; }

    /// <summary>How long the website is to be left alone for, where it has to be.</summary>
    public TimeSpan? LeftAloneFor { get; private set; }

    /// <summary>Whether this pass may start asking the website for anything else.</summary>
    public bool MayAskForMore => LeftAloneFor is null && _clock.GetElapsedTime(_began) < _budget;

    /// <summary>
    /// Takes in what a website said about being asked again.
    /// </summary>
    /// <param name="fetched">What it answered.</param>
    public void Hear(Fetched fetched)
    {
        ArgumentNullException.ThrowIfNull(fetched);

        Hear(fetched.Outcome, fetched.RetryAfter);
    }

    /// <summary>
    /// Takes in what a website's answer for its robots.txt said about being asked again.
    /// </summary>
    /// <param name="verdict">What came of asking about an address.</param>
    public void Hear(RobotsVerdict verdict) => Hear(verdict.Trouble, verdict.RetryAfter);

    /// <summary>Stops this pass asking the website for anything else, because it is in trouble.</summary>
    public void LeaveAloneAfterFailure() => LeaveAloneFor(_afterFailure);

    private void Hear(FetchOutcome? outcome, TimeSpan? retryAfter)
    {
        var asked = outcome == FetchOutcome.AskedToSlowDown ? retryAfter ?? _afterFailure : retryAfter;

        if (asked is { } wait)
        {
            LeaveAloneFor(wait);
        }
    }

    private void LeaveAloneFor(TimeSpan wait)
    {
        var fair = wait < LongestLeftAlone ? wait : LongestLeftAlone;

        if (LeftAloneFor is not { } already || fair > already)
        {
            LeftAloneFor = fair;
        }
    }
}
