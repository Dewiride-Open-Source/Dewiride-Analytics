using System.Text;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads a website's sitemap from the top: the address somebody named, then each file it leads to.
/// </summary>
/// <remarks>
/// <para>
/// A reading can span several passes. Once the address has been read the reading is open, and it stays
/// open while there are parts to ask for. Budget running out, or a website asking to be left alone, only
/// means the next pass picks up where this one stopped. It closes when nothing is left to ask for, and
/// only then says how it went.
/// </para>
/// <para>
/// An attempt is written down before anything is asked of the website, whether the address or a part, so
/// that nothing can bring the same request back a few seconds later: not a corrupt body, however it ends,
/// and not an engine that dies with it in hand. The attempt has gone, and the next is not due until the next
/// reading. It also makes a fault here a line in the log and not a loop: whatever was written is what
/// stands, and the reading moves on to what is next.
/// </para>
/// <para>
/// A website in trouble is given up on for this reading once a few of its parts in a row have shown it,
/// and every part not yet asked for ends the reading the same way. Otherwise one whose every file drips
/// out at the last moment could keep a reader for itself, a pass after a pass, for as long as it lists
/// files. A file too large to read counts for the same reason.
/// </para>
/// <para>
/// A website that fails, or sends what cannot be read, is not a fault here. It comes back as an outcome
/// and goes where the website's owner can read it, and nothing about it reaches the log.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="writer">Writes what comes of it, under the claim.</param>
/// <param name="fetcher">Asks the website.</param>
/// <param name="options">How often, how long and how much.</param>
/// <param name="clock">The clock the reading's times are written by.</param>
/// <param name="logger">Log.</param>
internal sealed class SitemapCycle(
    ControlPlaneDbContext database,
    PostWriter writer,
    WebsiteFetcher fetcher,
    IOptions<SitemapOptions> options,
    TimeProvider clock,
    ILogger<SitemapCycle> logger)
{
    /// <summary>How many pages go to the database in one statement.</summary>
    private const int MostPagesInOneWrite = 1000;

    /// <summary>How many pages may be held for a website, for each it may list.</summary>
    private const int MostHeldForEachListed = 3;

    /// <summary>
    /// How many pages stay known once a reading closes, those no file of the sitemap vouches for any longer
    /// counted with those it does, for each page a website may list.
    /// </summary>
    private const int MostKeptForEachListed = 2;

    /// <summary>
    /// How much a page's path and address may take up, on average across the pages a website may list.
    /// </summary>
    /// <remarks>
    /// Several times what an ordinary post's take up, and enough for paths made of titles in a script an
    /// address spells each character of in nine. It bounds what a sitemap listing made-up addresses as long
    /// as the protocol allows can leave in the database, which counting pages alone does not.
    /// </remarks>
    private const int MostBytesForEachListed = 1024;

    /// <summary>How long a page stays known once a complete reading has found it gone.</summary>
    private static readonly TimeSpan KeptAfterUnlisting = TimeSpan.FromDays(365);

    private readonly SitemapOptions _settings = options.Value;

    /// <summary>
    /// Takes a reading as far as this pass can: starting one if its time has come, and carrying on with
    /// the one under way.
    /// </summary>
    /// <param name="pass">The pass.</param>
    /// <param name="cancellationToken">Stops the reading, which then ends as a cancellation.</param>
    /// <returns>A task that completes once it has gone as far as it can.</returns>
    public async Task AdvanceAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (pass.ReadingStartedAt is null && !await TryOpenAsync(pass, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await ReadPartsAsync(pass, cancellationToken).ConfigureAwait(false))
        {
            await CloseAsync(pass, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks for the parts waiting to be asked for, as many as this pass may.
    /// </summary>
    /// <param name="pass">The pass.</param>
    /// <param name="cancellationToken">Stops the asking, which then ends as a cancellation.</param>
    /// <returns>Whether that leaves nothing to ask for.</returns>
    private async Task<bool> ReadPartsAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        var parts = await database.SitemapParts
            .AsNoTracking()
            .Where(part => part.SiteId == pass.Lease.SiteId && part.ReadAt == null)
            .OrderBy(part => part.Id)
            .Select(part => new PartToRead(part.Id, part.Address, part.IsPosts, part.Etag, part.LastModified))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var troubleInARow = 0;

        foreach (var part in parts)
        {
            if (!pass.MayAskForMore)
            {
                return false;
            }

            var problem = await ReadPartAsync(pass, part, cancellationToken).ConfigureAwait(false);

            troubleInARow = problem is { } stoppedBy && CountsAsTrouble(stoppedBy) ? troubleInARow + 1 : 0;

            if (troubleInARow == SitemapPassContext.MostTroubleInARow)
            {
                await GiveUpOnTheRestAsync(pass, problem!.Value, cancellationToken).ConfigureAwait(false);

                return true;
            }
        }

        return true;
    }

    private async Task GiveUpOnTheRestAsync(SitemapPassContext pass, SitemapProblem problem, CancellationToken cancellationToken)
    {
        await writer
            .GiveUpOnTheRestAsync(pass.Lease, SitemapProblems.OutcomeOf(problem), clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        pass.LeaveAloneAfterFailure();
    }

    /// <summary>
    /// Asks for the address, if its time has come.
    /// </summary>
    /// <param name="pass">The pass.</param>
    /// <param name="cancellationToken">Stops the asking, which then ends as a cancellation.</param>
    /// <returns>Whether a reading is now under way.</returns>
    private async Task<bool> TryOpenAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (pass.NextReadingAt > now)
        {
            return false;
        }

        pass.NextReadingAt = now + _settings.RetryAfterFailure;

        await writer.BeginAddressAsync(pass.Lease, pass.NextReadingAt, now, cancellationToken).ConfigureAwait(false);

        pass.AddressInHand = true;

        try
        {
            return await AskForAddressAsync(pass, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsAFaultHere(failure, cancellationToken))
        {
            SitemapLog.ItemFailed(logger, pass.Lease.SiteId, failure.GetType().Name);

            return await FailAsync(pass, SitemapProblem.TooSlow, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> AskForAddressAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        var verdict = await pass.Robots.AskAsync(pass.Sitemap, cancellationToken).ConfigureAwait(false);

        pass.Hear(verdict);

        if (!verdict.Permitted)
        {
            return await FailAsync(pass, RefusalOf(verdict), cancellationToken).ConfigureAwait(false);
        }

        using var fetched = await fetcher
            .FetchAsync(pass.Bounds, pass.Sitemap, FetchKind.Sitemap, pass.Validators, pass.Robots.AskAsync, cancellationToken)
            .ConfigureAwait(false);

        pass.Hear(fetched);

        return fetched.Outcome switch
        {
            FetchOutcome.Succeeded => await ReadAddressAsync(pass, fetched, cancellationToken).ConfigureAwait(false),
            FetchOutcome.NotModified => await KeepAddressAsync(pass, cancellationToken).ConfigureAwait(false),
            _ => await FailAsync(pass, SitemapProblems.Of(fetched.Outcome), cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<bool> ReadAddressAsync(
        SitemapPassContext pass,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        var opened = await OpenDocumentAsync(fetched).ConfigureAwait(false);

        if (opened.Document is not { } document)
        {
            return await FailAsync(pass, opened.Problem, cancellationToken).ConfigureAwait(false);
        }

        using (document)
        {
            return document.Kind == SitemapRootKind.Index
                ? await ReadIndexAsync(pass, document, fetched, cancellationToken).ConfigureAwait(false)
                : await ReadOnlyPartAsync(pass, document, fetched, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads a sitemap that is a list of parts, and opens a reading of those it says to read.
    /// </summary>
    /// <remarks>
    /// The whole list is taken in before anything is written, so a list that breaks partway through
    /// changes nothing. Asked for next time, it is only as good as the last one that was read whole. A
    /// list that leads nowhere but off the website has told a reading nothing, and that is not the same
    /// as having said the website has no posts.
    /// </remarks>
    private async Task<bool> ReadIndexAsync(
        SitemapPassContext pass,
        SitemapDocument document,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        var entries = new IndexEntries(pass.Bounds, _settings.MostSitemapParts);

        try
        {
            await foreach (var entry in document.EntriesAsync(fetched.Deadline).ConfigureAwait(false))
            {
                entries.Read(entry);
            }
        }
        catch (Exception failure) when (fetched.OutcomeOf(failure) is { } outcome)
        {
            return await FailAsync(pass, ProblemReading(fetched, outcome), cancellationToken).ConfigureAwait(false);
        }

        if (entries.Chosen.Count == 0 && entries.WentOffSite)
        {
            return await FailAsync(pass, SitemapProblem.LeftTheWebsite, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.GetUtcNow();
        var parts = entries.Chosen
            .Select(address => new NamedPart(Guid.CreateVersion7(now), address, entries.FoundPosts))
            .ToList();

        await writer.NamePartsAsync(pass.Lease, parts, cancellationToken).ConfigureAwait(false);

        var listProblem = ProblemOfTheList(entries, document);

        await OpenAsync(
                pass,
                new ReadingOpening(SitemapRootKind.Index, fetched.Validators, listProblem),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Reads a sitemap that is one list of pages, as the only part it has.
    /// </summary>
    /// <remarks>
    /// Its pages are already arriving, so they are read from this answer and not by asking for the same
    /// file a second time. What it said about itself is kept once all of it has been read.
    /// </remarks>
    private async Task<bool> ReadOnlyPartAsync(
        SitemapPassContext pass,
        SitemapDocument document,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var isPosts = PostsPart.Names(pass.Sitemap);
        var named = new NamedPart(Guid.CreateVersion7(now), pass.Lease.Address, isPosts);

        await writer.NamePartsAsync(pass.Lease, [named], cancellationToken).ConfigureAwait(false);

        await OpenAsync(
                pass,
                new ReadingOpening(SitemapRootKind.UrlSet, default, null),
                cancellationToken)
            .ConfigureAwait(false);

        var part = await database.SitemapParts
            .AsNoTracking()
            .Where(candidate => candidate.SiteId == pass.Lease.SiteId && candidate.Address == pass.Lease.Address)
            .Select(candidate => new PartToRead(
                candidate.Id,
                candidate.Address,
                candidate.IsPosts,
                candidate.Etag,
                candidate.LastModified))
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        await BeginPartAsync(pass, part, cancellationToken).ConfigureAwait(false);
        await ReadPagesAsync(pass, part, document, fetched, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Goes on from an address that has not changed since it was last read. That rests on knowing what it
    /// held then, and with no record of that there is nothing for its answer to stand for.
    /// </summary>
    private async Task<bool> KeepAddressAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        if (pass.ReadingKind is not { } kind)
        {
            return await FailAsync(pass, SitemapProblem.ServerError, cancellationToken).ConfigureAwait(false);
        }

        if (kind == SitemapRootKind.Index)
        {
            await writer.AskPartsAgainAsync(pass.Lease, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await writer.KeepPartsAsTheyAreAsync(pass.Lease, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        await OpenAsync(
                pass,
                new ReadingOpening(kind, pass.Validators, pass.ListProblem),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    private async Task OpenAsync(SitemapPassContext pass, ReadingOpening opening, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await writer.OpenReadingAsync(pass.Lease, opening, now, cancellationToken).ConfigureAwait(false);

        pass.ReadingStartedAt = now;
        pass.ListProblem = opening.Problem;
        pass.AddressInHand = false;
    }

    /// <summary>
    /// Records that the sitemap could not be read, and leaves everything already known as it is.
    /// </summary>
    /// <returns>That no reading is under way.</returns>
    private async Task<bool> FailAsync(SitemapPassContext pass, SitemapProblem problem, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var wait = pass.LeftAloneFor > _settings.RetryAfterFailure ? pass.LeftAloneFor.Value : _settings.RetryAfterFailure;

        if (SitemapProblems.IsTroubleAtTheWebsite(problem))
        {
            pass.LeaveAloneAfterFailure();
        }

        pass.NextReadingAt = now + wait;

        await writer.FailReadingAsync(pass.Lease, problem, pass.NextReadingAt, now, cancellationToken).ConfigureAwait(false);

        pass.ReadingStartedAt = null;
        pass.AddressInHand = false;
        pass.PartInHand = null;

        SitemapLog.ReadingEnded(logger, pass.Lease.SiteId, SitemapState.Failed, problem);

        return false;
    }

    private async Task BeginPartAsync(SitemapPassContext pass, PartToRead part, CancellationToken cancellationToken)
    {
        await writer.BeginPartAsync(pass.Lease, part.Id, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        pass.PartInHand = part.Id;
    }

    /// <summary>
    /// Asks for one part, and whatever happens goes on to the next.
    /// </summary>
    /// <returns>What stopped it being read, where something did.</returns>
    private async Task<SitemapProblem?> ReadPartAsync(
        SitemapPassContext pass,
        PartToRead part,
        CancellationToken cancellationToken)
    {
        await BeginPartAsync(pass, part, cancellationToken).ConfigureAwait(false);

        try
        {
            return await AskForPartAsync(pass, part, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsAFaultHere(failure, cancellationToken))
        {
            SitemapLog.ItemFailed(logger, pass.Lease.SiteId, failure.GetType().Name);

            pass.PartInHand = null;

            return SitemapProblem.TooSlow;
        }
    }

    private async Task<SitemapProblem?> AskForPartAsync(
        SitemapPassContext pass,
        PartToRead part,
        CancellationToken cancellationToken)
    {
        var address = new Uri(part.Address);
        var verdict = await pass.Robots.AskAsync(address, cancellationToken).ConfigureAwait(false);

        pass.Hear(verdict);

        if (!verdict.Permitted)
        {
            return await SettlePartAsync(pass, part, RefusalOf(verdict), cancellationToken).ConfigureAwait(false);
        }

        using var fetched = await fetcher
            .FetchAsync(pass.Bounds, address, FetchKind.Sitemap, part.Validators, pass.Robots.AskAsync, cancellationToken)
            .ConfigureAwait(false);

        pass.Hear(fetched);

        return fetched.Outcome switch
        {
            FetchOutcome.Succeeded => await ReadPartBodyAsync(pass, part, fetched, cancellationToken).ConfigureAwait(false),
            FetchOutcome.NotModified => await KeepPartAsync(pass, part, cancellationToken).ConfigureAwait(false),
            _ => await SettlePartAsync(pass, part, SitemapProblems.Of(fetched.Outcome), cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<SitemapProblem?> KeepPartAsync(SitemapPassContext pass, PartToRead part, CancellationToken cancellationToken)
    {
        await writer
            .SettlePartAsync(pass.Lease, part.Id, SitemapPartOutcome.Unchanged, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        pass.PartInHand = null;

        return null;
    }

    /// <summary>
    /// Reads a part that answered. A part that turns out to be an index of its own is not read: an index
    /// leads to lists of pages, and one that leads to indexes could go on for as long as it likes.
    /// </summary>
    private async Task<SitemapProblem?> ReadPartBodyAsync(
        SitemapPassContext pass,
        PartToRead part,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        var opened = await OpenDocumentAsync(fetched).ConfigureAwait(false);

        if (opened.Document is not { } document)
        {
            return await SettlePartAsync(pass, part, opened.Problem, cancellationToken).ConfigureAwait(false);
        }

        using (document)
        {
            return document.Kind == SitemapRootKind.UrlSet
                ? await ReadPagesAsync(pass, part, document, fetched, cancellationToken).ConfigureAwait(false)
                : await SettlePartAsync(pass, part, SitemapProblem.NotASitemap, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stores the pages a part lists, a batch at a time, and says what came of it.
    /// </summary>
    /// <remarks>
    /// Whatever arrived before something went wrong stays. Those pages are real, and only the part being
    /// stamped says it was read to the end, so this cannot be mistaken for having seen all of it.
    /// </remarks>
    /// <returns>What stopped all of it being read, where something did.</returns>
    private async Task<SitemapProblem?> ReadPagesAsync(
        SitemapPassContext pass,
        PartToRead part,
        SitemapDocument document,
        Fetched fetched,
        CancellationToken cancellationToken)
    {
        var generation = Guid.CreateVersion7(clock.GetUtcNow());
        var listing = new PartListing(part.Id, generation, part.IsPosts);
        var entries = new PageEntries(pass.Bounds);
        var batch = new Dictionary<string, PostListing>(StringComparer.Ordinal);
        var room = await RoomForPagesAsync(pass, part, cancellationToken).ConfigureAwait(false);
        var bytesLeft = room.Bytes;
        var stored = 0;
        var cutShort = false;
        SitemapProblem? problem = null;

        try
        {
            await foreach (var entry in document.EntriesAsync(fetched.Deadline).ConfigureAwait(false))
            {
                if (entries.Read(entry) is not { } page)
                {
                    continue;
                }

                var bytes = Encoding.UTF8.GetByteCount(page.Path) + Encoding.UTF8.GetByteCount(page.Address);

                if (stored + batch.Count >= room.Pages || bytes > bytesLeft)
                {
                    cutShort = true;
                    break;
                }

                bytesLeft -= bytes;
                batch[page.Path] = page;

                if (batch.Count == MostPagesInOneWrite)
                {
                    stored += await SaveBatchAsync(pass, listing, batch, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception failure) when (failure is not SitemapClaimLostException && fetched.OutcomeOf(failure) is { } outcome)
        {
            problem = ProblemReading(fetched, outcome);
        }

        stored += await SaveBatchAsync(pass, listing, batch, cancellationToken).ConfigureAwait(false);

        if (problem is { } stoppedBy)
        {
            return await SettlePartAsync(pass, part, stoppedBy, cancellationToken).ConfigureAwait(false);
        }

        if (cutShort || document.ListsMoreThanIsRead)
        {
            await writer.CutPartShortAsync(pass.Lease, part.Id, stored, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            pass.PartInHand = null;

            return SitemapProblem.TooMany;
        }

        var reading = new PartReading(generation, fetched.Validators, stored, entries.OffSite, entries.WithQueries);

        await FinishPartAsync(pass, part, reading, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task FinishPartAsync(
        SitemapPassContext pass,
        PartToRead part,
        PartReading reading,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await writer.FinishPartAsync(pass.Lease, part.Id, reading, now, cancellationToken).ConfigureAwait(false);

        pass.PartInHand = null;

        if (part.Address == pass.Lease.Address)
        {
            await writer.RememberAddressAsync(pass.Lease, reading.Validators, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> SaveBatchAsync(
        SitemapPassContext pass,
        PartListing listing,
        Dictionary<string, PostListing> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await writer
            .ListPagesAsync(pass.Lease, listing, batch.Values, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        var saved = batch.Count;

        batch.Clear();

        return saved;
    }

    private async Task<SitemapProblem?> SettlePartAsync(
        SitemapPassContext pass,
        PartToRead part,
        SitemapProblem problem,
        CancellationToken cancellationToken)
    {
        await writer
            .SettlePartAsync(pass.Lease, part.Id, SitemapProblems.OutcomeOf(problem), clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        pass.PartInHand = null;

        return problem;
    }

    /// <summary>
    /// How many pages a part may list, and how much their paths and addresses may take up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The limit on what a website lists is counted across its parts, so splitting a list into files does
    /// not get past it. That counts what its parts said, though, and says nothing about what is actually
    /// in the database: a sitemap that breaks at the end of the file every time, listing different pages
    /// each time, is never read to its end, so nothing ever comes off the list. So there is a limit on
    /// that too, in pages and in bytes, which leaves room for a full list and as much again twice over.
    /// </para>
    /// <para>
    /// It is a ceiling and not a trap: every reading that closes forgets the oldest of the pages no file
    /// vouches for any longer, down to what leaves a whole list room, so a sitemap put right is read in full
    /// at its next reading.
    /// </para>
    /// </remarks>
    private async Task<Room> RoomForPagesAsync(SitemapPassContext pass, PartToRead part, CancellationToken cancellationToken)
    {
        var listedElsewhere = await database.SitemapParts
            .AsNoTracking()
            .Where(other => other.SiteId == pass.Lease.SiteId && other.Id != part.Id)
            .SumAsync(other => other.Entries, cancellationToken)
            .ConfigureAwait(false);

        var held = await database.Database
            .SqlQuery<HeldPages>(
                $"""
                SELECT count(*)::int AS pages,
                       COALESCE(sum(octet_length(path) + octet_length(address)), 0)::bigint AS bytes
                FROM site_posts
                WHERE site_id = {pass.Lease.SiteId}
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var most = _settings.MostPostsPerSite;

        return new Room(
            Math.Min(most - listedElsewhere, (MostHeldForEachListed * most) - held.Pages),
            ((long)MostHeldForEachListed * most * MostBytesForEachListed) - held.Bytes);
    }

    /// <summary>
    /// Closes a reading with nothing left to ask for, and says how it went.
    /// </summary>
    private async Task CloseAsync(SitemapPassContext pass, CancellationToken cancellationToken)
    {
        var parts = await database.SitemapParts
            .AsNoTracking()
            .Where(part => part.SiteId == pass.Lease.SiteId)
            .OrderBy(part => part.Id)
            .Select(part => new PartStanding(
                part.Outcome ?? SitemapPartOutcome.TooSlow,
                part.Entries,
                part.OffSiteEntries,
                part.QueryEntries))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var verdict = SitemapVerdict.Of(parts, pass.ListProblem);
        var now = clock.GetUtcNow();

        if (verdict.Complete)
        {
            await writer.UnlistAsync(pass.Lease, now, cancellationToken).ConfigureAwait(false);
        }

        await writer
            .ForgetUnlistedAsync(pass.Lease, now - KeptAfterUnlisting, _settings.MostPostsPerSite, cancellationToken)
            .ConfigureAwait(false);

        await writer
            .ForgetUnvouchedAsync(
                pass.Lease,
                MostKeptForEachListed * _settings.MostPostsPerSite,
                (long)MostKeptForEachListed * _settings.MostPostsPerSite * MostBytesForEachListed,
                cancellationToken)
            .ConfigureAwait(false);

        var failed = verdict.State == SitemapState.Failed;

        pass.NextReadingAt = failed
            ? now + _settings.RetryAfterFailure
            : (pass.ReadingStartedAt ?? now) + _settings.Cadence;

        var closing = new ReadingClosing(verdict.State, verdict.Problem, failed ? null : now, pass.NextReadingAt);

        await writer.CloseReadingAsync(pass.Lease, closing, now, cancellationToken).ConfigureAwait(false);

        pass.ReadingStartedAt = null;

        if (failed && verdict.Problem is { } problem && SitemapProblems.IsTroubleAtTheWebsite(problem))
        {
            pass.LeaveAloneAfterFailure();
        }

        SitemapLog.ReadingEnded(logger, pass.Lease.SiteId, verdict.State, verdict.Problem);
    }

    /// <summary>
    /// Begins reading a body as a sitemap.
    /// </summary>
    private static async Task<OpenedDocument> OpenDocumentAsync(Fetched fetched)
    {
        try
        {
            var document = await SitemapDocument.OpenAsync(fetched.Body).ConfigureAwait(false);

            if (document.Kind is not null)
            {
                return new OpenedDocument(document, default);
            }

            document.Dispose();

            return new OpenedDocument(null, SitemapProblem.NotASitemap);
        }
        catch (Exception failure) when (fetched.OutcomeOf(failure) is { } outcome)
        {
            return new OpenedDocument(null, ProblemReading(fetched, outcome));
        }
    }

    /// <summary>
    /// What a failure partway through a body comes to. A website that began answering and never finished,
    /// in the time it had, has been too slow, and did answer.
    /// </summary>
    private static SitemapProblem ProblemReading(Fetched fetched, FetchOutcome outcome) =>
        fetched.Deadline.IsCancellationRequested ? SitemapProblem.TooSlow : SitemapProblems.Of(outcome);

    private static SitemapProblem? ProblemOfTheList(IndexEntries entries, SitemapDocument document)
    {
        if (entries.CutShort || document.ListsMoreThanIsRead)
        {
            return SitemapProblem.TooMany;
        }

        return entries.WentOffSite ? SitemapProblem.LeftTheWebsite : null;
    }

    /// <summary>
    /// Whether a part's problem counts towards giving up on the website for this reading: trouble at the
    /// website, or a file too large to read, which costs as much to find out again.
    /// </summary>
    private static bool CountsAsTrouble(SitemapProblem problem) =>
        problem == SitemapProblem.TooLarge || SitemapProblems.IsTroubleAtTheWebsite(problem);

    /// <summary>
    /// What robots.txt not letting something be read comes to: its asking for that, or nothing being known
    /// because the file itself could not be read.
    /// </summary>
    private static SitemapProblem RefusalOf(RobotsVerdict verdict) =>
        verdict.Trouble is { } trouble ? SitemapProblems.Of(trouble) : SitemapProblem.RobotsDisallowed;

    /// <summary>
    /// Whether what was thrown is for this reading to write down and move on from: anything but the claim
    /// having gone, which stops the pass, or the engine stopping, which stops everything.
    /// </summary>
    private static bool IsAFaultHere(Exception failure, CancellationToken cancellationToken) =>
        failure is not SitemapClaimLostException && !cancellationToken.IsCancellationRequested;

    /// <summary>What a website holds already.</summary>
    /// <param name="Pages">How many pages.</param>
    /// <param name="Bytes">How much their paths and addresses take up.</param>
    private sealed record HeldPages(int Pages, long Bytes);

    /// <summary>How much more one part may store.</summary>
    /// <param name="Pages">How many more pages.</param>
    /// <param name="Bytes">How much more their paths and addresses may take up.</param>
    private readonly record struct Room(int Pages, long Bytes);

    /// <summary>
    /// One part waiting to be asked for.
    /// </summary>
    /// <param name="Id">The part.</param>
    /// <param name="Address">Its address.</param>
    /// <param name="IsPosts">Whether it is recognised as holding the website's posts.</param>
    /// <param name="ETag">The entity tag it last answered with.</param>
    /// <param name="LastModified">When it last said it changed.</param>
    private sealed record PartToRead(Guid Id, string Address, bool IsPosts, string? ETag, DateTimeOffset? LastModified)
    {
        /// <summary>What to ask with, to be told if nothing has changed.</summary>
        public WebsiteValidators Validators => new(ETag, LastModified);
    }

    /// <summary>
    /// What came of beginning to read a body.
    /// </summary>
    /// <param name="Document">The document, where it is a sitemap.</param>
    /// <param name="Problem">What it is instead, where it is not.</param>
    private readonly record struct OpenedDocument(SitemapDocument? Document, SitemapProblem Problem);
}
