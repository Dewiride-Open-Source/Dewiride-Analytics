using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the control-plane schema applies to an empty database, stays applied, and carries the
/// rows an earlier schema already holds through a migration that changes their table.
/// </summary>
/// <remarks>
/// Migrations are forward-only and run on somebody else's database with nobody to call when they
/// go wrong. So every schema is proved from nothing, rather than on a machine that happened to
/// hold the one before it; and where a migration has to fill a new column from what is already
/// there, a database of the test's own is taken to the schema before it, given rows in that
/// schema's own shape, and taken forward.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SchemaTests(AnalyticsStackFixture stack)
{
    private const string TableNamesSql = """
        SELECT table_name AS "Value"
        FROM information_schema.tables
        WHERE table_schema = 'public'
        """;

    private const string ColumnNamesSql = """
        SELECT column_name AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = {0}
        """;

    private const string OrganizationIndexNamesSql = """
        SELECT indexname AS "Value"
        FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'organizations'
        """;

    private const string InstallationClaimCheckConstraintsSql = """
        SELECT constraint_name AS "Value"
        FROM information_schema.table_constraints
        WHERE table_schema = 'public'
          AND table_name = 'installation_claims'
          AND constraint_type = 'CHECK'
        """;

    private const string DeleteRuleSql = """
        SELECT delete_rule AS "Value"
        FROM information_schema.referential_constraints
        WHERE constraint_schema = 'public' AND constraint_name = {0}
        """;

    private const string ReadThroughColumnSql = """
        SELECT is_nullable || ' ' || (column_default IS NOT NULL)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'classification_progress'
          AND column_name = 'read_through'
        """;

    private const string IndexNamesSql = """
        SELECT indexname AS "Value"
        FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = {0}
        """;

    private const string SitemapAddressColumnSql = """
        SELECT data_type || ' ' || character_maximum_length || ' ' || is_nullable AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'sites'
          AND column_name = 'sitemap_address'
        """;

    /// <summary>The migration applied before a bookmark kept how far it had read.</summary>
    private const string BeforeReadThrough = "20260921061732_AddOrganizationClosure";

    private const string BeforeClicksStartOff = "20261024155457_AddClassificationReadThrough";

    /// <summary>The migration applied before a website could name a sitemap.</summary>
    private const string BeforeSitemapReading = "20261025124658_StopRecordingClicksUntilAsked";

    [Fact]
    public async Task Every_Control_Plane_Table_Was_Created()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(["organizations", "sites", "site_memberships", "visitor_key_salts"]);
    }

    /// <summary>
    /// Self-hosters read their own database. Identity and the authorisation server both ship
    /// tables named for their libraries rather than for this product, and both were renamed.
    /// </summary>
    [Fact]
    public async Task The_Account_Tables_Carry_This_Product_Names()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(
            ["users", "roles", "user_roles", "user_claims", "user_logins", "user_tokens", "role_claims"]);
        tables.Should().NotContain(name => name.StartsWith("asp_net", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Authorisation_Server_Tables_Carry_This_Product_Names()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(
        [
            "openiddict_applications",
            "openiddict_authorizations",
            "openiddict_scopes",
            "openiddict_tokens",
        ]);
    }

    [Fact]
    public async Task Every_Column_Is_Named_The_Way_Somebody_Would_Type_It()
    {
        var columns = await ColumnNamesAsync("sites");

        columns.Should().Contain(
            ["organization_id", "display_name", "time_zone_id", "retain_query_strings", "allowed_origins"]);
    }

    [Fact]
    public async Task An_Organisation_Records_When_It_Was_Closed_By_Whom_And_Whether_It_Was_Warned()
    {
        var columns = await ColumnNamesAsync("organizations");

        columns.Should().Contain(["closed_at", "closed_by_user_id", "deletion_reminder_sent_at"]);
    }

    /// <summary>
    /// The sweep asks every hour for the closed organisations whose time has run, and the closer
    /// is joined whenever a closure is explained; neither read should have to scan the table.
    /// </summary>
    [Fact]
    public async Task The_Columns_A_Closure_Is_Found_By_Are_Indexed()
    {
        var indexes = await QueryAsync(OrganizationIndexNamesSql);

        indexes.Should().Contain(["ix_organizations_closed_at", "ix_organizations_closed_by_user_id"]);
    }

    [Fact]
    public async Task The_Claim_On_An_Install_Is_Recorded_In_A_Table_Of_Its_Own()
    {
        var tables = await TableNamesAsync();
        var columns = await ColumnNamesAsync("installation_claims");

        tables.Should().Contain("installation_claims");
        columns.Should().BeEquivalentTo("id", "claimed_at");
    }

    [Fact]
    public async Task The_Database_Holds_The_Claim_To_A_Single_Row()
    {
        var constraints = await QueryAsync(InstallationClaimCheckConstraintsSql);

        constraints.Should().Contain("ck_installation_claims_one_row");
    }

    /// <summary>
    /// A second row is refused by the database rather than by whichever code happens to write
    /// the table — the first-run claim, the purge that empties an install, or a migration — so
    /// none of them can turn "claimed" into a count that has to be reasoned about.
    /// </summary>
    [Fact]
    public async Task A_Second_Claim_Row_Is_Refused_By_The_Database()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var act = async () => await database.Database.ExecuteSqlAsync(
            $"INSERT INTO installation_claims (id, claimed_at) VALUES (2, {now})",
            Cancellation.Token);

        var refused = await act.Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    /// <summary>
    /// Deleting a person must not delete what they did. An organisation they closed stays closed
    /// and an invitation they sent into another organisation stays usable, each with the reference
    /// to them cleared rather than the row taken with them.
    /// </summary>
    [Theory]
    [InlineData("fk_organizations_users_closed_by_user_id")]
    [InlineData("fk_organization_invitations_users_invited_by_user_id")]
    public async Task Deleting_A_Person_Clears_The_Reference_To_Them_Rather_Than_Cascading(string constraint)
    {
        var rules = await QueryAsync(DeleteRuleSql, constraint);

        rules.Should().Equal("SET NULL");
    }

    /// <summary>
    /// Applying again is what a self-hoster's every restart does.
    /// </summary>
    [Fact]
    public async Task Applying_The_Migrations_Again_Changes_Nothing()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var before = await database.Database.GetAppliedMigrationsAsync(Cancellation.Token);
        await database.Database.MigrateAsync(Cancellation.Token);
        var after = await database.Database.GetAppliedMigrationsAsync(Cancellation.Token);

        before.Should().NotBeEmpty();
        after.Should().Equal(before);
        (await database.Database.GetPendingMigrationsAsync(Cancellation.Token)).Should().BeEmpty();
    }

    /// <summary>
    /// How far a bookmark has read is always recorded. A release that does not know the column —
    /// one an installation has been rolled back to — can still start a bookmark, because the
    /// column has a default; what a bookmark started that way reads as is proved with the store.
    /// </summary>
    [Fact]
    public async Task A_Bookmark_Always_Records_How_Far_It_Has_Read()
    {
        var column = await QueryAsync(ReadThroughColumnSql);

        column.Should().Equal("NO true");
    }

    /// <summary>
    /// Migrations run on a database that already holds bookmarks. Each of them comes through with
    /// its reading recorded as exactly as far as it had judged.
    /// </summary>
    [Fact]
    public async Task Bookmarks_Already_Kept_Read_As_Far_As_They_Judged_Once_The_Reading_Is_Recorded()
    {
        var judged = new DateTimeOffset(2026, 5, 1, 6, 0, 0, TimeSpan.Zero);

        await using var database = OwnDatabase("bookmarks");

        try
        {
            var migrator = database.GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeReadThrough, Cancellation.Token);

            // Written in the shape the tables had then, never through today's model, which names
            // whatever columns later migrations add.
            var organizationId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO organizations (id, name, created_at)
                VALUES ({organizationId}, 'Owner of an older bookmark', {judged})
                """,
                Cancellation.Token);

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO sites
                    (id, organization_id, domain, display_name, time_zone_id, created_at,
                     capture_clicks, retain_query_strings, allowed_origins)
                VALUES
                    ({siteId}, {organizationId}, 'older.example.com', 'older.example.com', 'Etc/UTC', {judged},
                     false, false, ARRAY[]::text[])
                """,
                Cancellation.Token);

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO classification_progress
                    (site_id, ruleset_major, ruleset_minor, classified_through, updated_at)
                VALUES ({siteId}, 9, 0, {judged}, {judged})
                """,
                Cancellation.Token);

            await migrator.MigrateAsync(cancellationToken: Cancellation.Token);

            var bookmark = await database.ClassificationProgress.AsNoTracking().SingleAsync(Cancellation.Token);

            bookmark.ClassifiedThrough.Should().Be(judged);
            bookmark.ReadThrough.Should().Be(judged);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync(Cancellation.Token);
        }
    }

    /// <summary>
    /// Every website comes through the upgrade with recording what people press switched off, as
    /// one added from then on starts: the words on its buttons can be its visitors' own, and only
    /// somebody who knows the site can say they are not.
    /// </summary>
    [Fact]
    public async Task Websites_That_Recorded_Presses_Stop_Once_The_Upgrade_Runs()
    {
        var added = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

        await using var database = OwnDatabase("presses");

        try
        {
            var migrator = database.GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeClicksStartOff, Cancellation.Token);

            var organizationId = Guid.NewGuid();

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO organizations (id, name, created_at)
                VALUES ({organizationId}, 'Owner of two older websites', {added})
                """,
                Cancellation.Token);

            foreach (var (domain, recording) in new[] { ("recording.example.com", true), ("quiet.example.com", false) })
            {
                await database.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO sites
                        (id, organization_id, domain, display_name, time_zone_id, created_at,
                         capture_clicks, retain_query_strings, allowed_origins)
                    VALUES
                        ({Guid.NewGuid()}, {organizationId}, {domain}, {domain}, 'Etc/UTC', {added},
                         {recording}, false, ARRAY[]::text[])
                    """,
                    Cancellation.Token);
            }

            await migrator.MigrateAsync(cancellationToken: Cancellation.Token);

            var recordingNow = await database.Database
                .SqlQueryRaw<bool>("SELECT capture_clicks AS \"Value\" FROM sites")
                .ToListAsync(Cancellation.Token);

            recordingNow.Should().HaveCount(2).And.OnlyContain(capture => !capture);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync(Cancellation.Token);
        }
    }

    /// <summary>
    /// A website written by something that does not say whether it records presses — a script, a
    /// restore, an insert by hand — records none, as one added through the product does.
    /// </summary>
    [Fact]
    public async Task A_Website_Written_Without_Saying_Whether_It_Records_Presses_Records_None()
    {
        var added = new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero);

        await using var database = OwnDatabase("unsaid");

        try
        {
            await database.Database.MigrateAsync(Cancellation.Token);

            var organizationId = Guid.NewGuid();

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO organizations (id, name, created_at)
                VALUES ({organizationId}, 'Owner of a website written by a script', {added})
                """,
                Cancellation.Token);

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO sites
                    (id, organization_id, domain, display_name, time_zone_id, created_at,
                     retain_query_strings, allowed_origins)
                VALUES
                    ({Guid.NewGuid()}, {organizationId}, 'scripted.example.com', 'scripted.example.com',
                     'Etc/UTC', {added}, false, ARRAY[]::text[])
                """,
                Cancellation.Token);

            var recording = await database.Database
                .SqlQueryRaw<bool>("SELECT capture_clicks AS \"Value\" FROM sites")
                .SingleAsync(Cancellation.Token);

            recording.Should().BeFalse();
        }
        finally
        {
            await database.Database.EnsureDeletedAsync(Cancellation.Token);
        }
    }

    /// <summary>
    /// What a person chose lives on the website's own row, and is the width an address can be. What
    /// the engine writes as it reads lives elsewhere, so a settings save and a reading never touch one
    /// row and nothing the collector resolves a website through ever carries it.
    /// </summary>
    [Fact]
    public async Task What_Was_Read_From_A_Sitemap_Is_Kept_In_Tables_Of_Its_Own()
    {
        var tables = await TableNamesAsync();
        var address = await QueryAsync(SitemapAddressColumnSql);
        var onTheWebsite = await ColumnNamesAsync("sites");

        tables.Should().Contain(["sitemap_reads", "sitemap_parts", "site_posts"]);
        address.Should().Equal($"character varying {Site.MaxSitemapAddressLength} YES");
        onTheWebsite.Should().NotContain(["next_pass_at", "claim_token", "state", "root_etag"]);
    }

    /// <summary>
    /// Every table of a reading says what it describes and carries what the reader's claim and its
    /// conditional requests rest on. The reader's statements name these columns in SQL, where nothing
    /// would notice one of them going missing until a reading failed.
    /// </summary>
    [Theory]
    [InlineData(
        "sitemap_reads",
        "site_id", "address", "host", "claim_token", "claimed_until", "next_pass_at", "next_cycle_at",
        "cycle_started_at", "state", "problem", "read_at", "succeeded_at", "root_kind", "root_etag",
        "root_last_modified", "root_problem", "updated_at")]
    [InlineData(
        "sitemap_parts",
        "id", "site_id", "address", "is_posts", "generation", "etag", "last_modified", "read_at", "outcome",
        "entries", "off_site_entries", "query_entries")]
    [InlineData(
        "site_posts",
        "site_id", "path", "address", "landed_path", "part_id", "part_generation", "listed_in_posts_part", "listed_modified_at",
        "first_listed_at", "unlisted_at", "title", "title_source", "published_at", "published_source", "page_type",
        "is_post", "page_due_at", "page_attempts", "page_read_at", "page_outcome", "page_read_lastmod", "page_etag",
        "page_last_modified")]
    public async Task A_Reading_Is_Kept_Under_The_Columns_The_Reader_Names(string table, params string[] columns)
    {
        (await ColumnNamesAsync(table)).Should().BeEquivalentTo(columns);
    }

    /// <summary>
    /// The indexes a reading is found by, and no others: one that nothing reads is a cost on every
    /// write with nothing to show for it.
    /// </summary>
    [Theory]
    [InlineData(
        "sitemap_reads",
        "pk_sitemap_reads", "ix_sitemap_reads_next_pass_at", "ix_sitemap_reads_host_claimed_until")]
    [InlineData("sitemap_parts", "pk_sitemap_parts", "ux_sitemap_parts_site_address")]
    [InlineData("site_posts", "pk_site_posts", "ix_site_posts_page_due", "ix_site_posts_part_id")]
    public async Task A_Reading_Is_Found_By_These_Indexes_And_No_Others(string table, params string[] indexes)
    {
        (await QueryAsync(IndexNamesSql, table)).Should().BeEquivalentTo(indexes);
    }

    /// <summary>
    /// A row that outlived the website it was read for could never be reached again, so it goes with
    /// the website, and with an account closed for good, by cascade from the one row that is deleted.
    /// </summary>
    [Theory]
    [InlineData("fk_sitemap_reads_sites_site_id")]
    [InlineData("fk_sitemap_parts_sites_site_id")]
    [InlineData("fk_site_posts_sites_site_id")]
    public async Task Removing_A_Website_Removes_What_Was_Read_From_Its_Sitemap(string constraint)
    {
        (await QueryAsync(DeleteRuleSql, constraint)).Should().Equal("CASCADE");
    }

    /// <summary>
    /// A post is keyed by its path, and the next complete reading finds it listed again or finds it
    /// gone. A sitemap that drops one of its files, or an address changed for another, must not take
    /// a website's posts away in the meantime.
    /// </summary>
    [Fact]
    public async Task A_Post_Outlives_The_Sitemap_Part_It_Was_Listed_In()
    {
        (await QueryAsync(DeleteRuleSql, "fk_site_posts_sitemap_parts_part_id")).Should().Equal("SET NULL");

        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var address = $"https://{site.Domain}/sitemap.xml";

        await SitemapSeed.AddReadingAsync(stack, site.Id, address);

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        await database.SitemapParts
            .Where(part => part.SiteId == site.Id)
            .ExecuteDeleteAsync(Cancellation.Token);

        var post = await database.SitePosts
            .AsNoTracking()
            .SingleAsync(candidate => candidate.SiteId == site.Id, Cancellation.Token);

        post.Path.Should().Be("/posts/hello");
        post.PartId.Should().BeNull();
    }

    /// <summary>
    /// What makes a page a post, worked out by the database so that no statement that counts posts can
    /// get it wrong. The second row is the one that matters most: a page outside the posts part that
    /// has not been read has said nothing, and a plain comparison with what it has not said is neither
    /// true nor false, in a column that may not be empty. Written that way, the row could not be stored
    /// at all and neither could anything else in the same statement.
    /// </summary>
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, null, null, false)]
    [InlineData(false, PostPageType.Article, PostPageOutcome.Read, true)]
    [InlineData(true, PostPageType.Article, PostPageOutcome.Read, true)]
    [InlineData(true, PostPageType.Other, PostPageOutcome.Read, false)]
    [InlineData(false, PostPageType.Other, PostPageOutcome.Read, false)]
    [InlineData(true, PostPageType.Article, PostPageOutcome.Gone, false)]
    [InlineData(true, null, PostPageOutcome.Gone, false)]
    [InlineData(true, null, PostPageOutcome.TurnedAway, true)]
    [InlineData(false, null, PostPageOutcome.Read, false)]
    public async Task A_Page_Is_A_Post_By_What_It_Says_And_Where_It_Is_Listed(
        bool listedInPostsPart,
        PostPageType? pageType,
        PostPageOutcome? pageOutcome,
        bool isPost)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = SitemapSeed.Now(stack);

        await SitemapSeed.WriteAsync(
            stack,
            new SitePost
            {
                SiteId = site.Id,
                Path = "/posts/hello",
                Address = $"https://{site.Domain}/posts/hello",
                ListedInPostsPart = listedInPostsPart,
                PageType = pageType,
                PageOutcome = pageOutcome,
                FirstListedAt = at,
            });

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var stored = await database.SitePosts
            .AsNoTracking()
            .SingleAsync(post => post.SiteId == site.Id, Cancellation.Token);

        stored.IsPost.Should().Be(isPost);
    }

    /// <summary>
    /// The longest address the reader will keep has to fit every index it sits in, not only its column.
    /// PostgreSQL refuses an index entry past about a third of a page, and only when the row arrives.
    /// </summary>
    [Fact]
    public async Task The_Longest_Address_Fits_Everywhere_It_Is_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = SitemapSeed.Now(stack);
        var address = LongestAddress(site.Domain);
        var path = new Uri(address).AbsolutePath;

        await SitemapSeed.WriteAsync(
            stack,
            new SitemapPart { Id = Guid.CreateVersion7(at), SiteId = site.Id, Address = address },
            new SitePost { SiteId = site.Id, Path = path, Address = address, FirstListedAt = at });

        (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(0, 1, 1));
    }

    /// <summary>
    /// A word that does not fit its column fails the statement writing it, which on a reading is the
    /// one line that says how it went. Every member a column can be given has to fit.
    /// </summary>
    [Fact]
    public async Task Every_Word_A_Reading_Stores_Fits_The_Column_It_Is_Stored_In()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>().Model;

        ShouldFit<SitemapRead, SitemapState>(model, nameof(SitemapRead.State));
        ShouldFit<SitemapRead, SitemapProblem>(model, nameof(SitemapRead.Problem));
        ShouldFit<SitemapRead, SitemapRootKind>(model, nameof(SitemapRead.RootKind));
        ShouldFit<SitemapRead, SitemapProblem>(model, nameof(SitemapRead.RootProblem));
        ShouldFit<SitemapPart, SitemapPartOutcome>(model, nameof(SitemapPart.Outcome));
        ShouldFit<SitePost, PostTitleSource>(model, nameof(SitePost.TitleSource));
        ShouldFit<SitePost, PostDateSource>(model, nameof(SitePost.PublishedSource));
        ShouldFit<SitePost, PostPageType>(model, nameof(SitePost.PageType));
        ShouldFit<SitePost, PostPageOutcome>(model, nameof(SitePost.PageOutcome));
    }

    /// <summary>
    /// These are stored by name, so renaming a member leaves rows already written that nothing can
    /// read, and what makes a page a post is worked out in the database from two of them. Adding a
    /// word is free; changing one is a migration, and this is what makes that a conversation.
    /// </summary>
    [Fact]
    public void The_Words_A_Reading_Is_Stored_Under_Stay_As_They_Were_Written()
    {
        Enum.GetNames<SitemapState>().Should().Contain(["Waiting", "Read", "PartlyRead", "Failed"]);
        Enum.GetNames<SitemapProblem>().Should().Contain(
        [
            "Unreachable", "NotFound", "TurnedAway", "ServerError", "Insecure", "NotASitemap", "TooLarge",
            "LeftTheWebsite", "TooManyRedirects", "PrivateAddress", "TooSlow", "TooMany",
            "AddressesWithQueries", "RobotsDisallowed", "RobotsUnreadable",
        ]);
        Enum.GetNames<SitemapRootKind>().Should().Contain(["Index", "UrlSet"]);
        Enum.GetNames<SitemapPartOutcome>().Should().Contain(["Read", "Unchanged", "NotASitemap", "TooMany"]);
        Enum.GetNames<PostTitleSource>().Should().Contain(["OpenGraph", "StructuredData", "Twitter", "Document"]);
        Enum.GetNames<PostDateSource>().Should().Contain(
            ["OpenGraph", "StructuredData", "Microdata", "Microformat", "WordPressBlock"]);
        Enum.GetNames<PostPageType>().Should().Contain(["Article", "Other"]);
        Enum.GetNames<PostPageOutcome>().Should().Contain(["Read", "Unchanged", "Gone", "NotAPage"]);
    }

    /// <summary>
    /// A self-hoster's websites come through the upgrade with nothing named, so nothing is fetched on
    /// anybody's behalf until somebody who runs a website asks.
    /// </summary>
    [Fact]
    public async Task A_Website_Written_Before_Sitemaps_Has_None()
    {
        var added = SitemapSeed.Now(stack);

        await using var database = OwnDatabase("sitemaps");

        try
        {
            var migrator = database.GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeSitemapReading, Cancellation.Token);

            // Written in the shape the tables had then, never through today's model, which names
            // whatever columns later migrations add.
            var organizationId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO organizations (id, name, created_at)
                VALUES ({organizationId}, 'Owner of an older website', {added})
                """,
                Cancellation.Token);

            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO sites
                    (id, organization_id, domain, display_name, time_zone_id, created_at,
                     capture_clicks, retain_query_strings, allowed_origins)
                VALUES
                    ({siteId}, {organizationId}, 'older.example.com', 'older.example.com', 'Etc/UTC', {added},
                     true, false, ARRAY[]::text[])
                """,
                Cancellation.Token);

            await migrator.MigrateAsync(cancellationToken: Cancellation.Token);

            var website = await database.Sites.AsNoTracking().SingleAsync(Cancellation.Token);

            website.Id.Should().Be(siteId);
            website.CaptureClicks.Should().BeTrue();
            website.SitemapAddress.Should().BeNull();

            (await database.SitemapReads.CountAsync(Cancellation.Token)).Should().Be(0);
            (await database.SitemapParts.CountAsync(Cancellation.Token)).Should().Be(0);
            (await database.SitePosts.CountAsync(Cancellation.Token)).Should().Be(0);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync(Cancellation.Token);
        }
    }

    private static void ShouldFit<TEntity, TWords>(IModel model, string property)
        where TWords : struct, Enum
    {
        var width = model.FindEntityType(typeof(TEntity))?.FindProperty(property)?.GetMaxLength();

        width.Should().NotBeNull();
        Enum.GetNames<TWords>().Should().OnlyContain(word => word.Length <= width);
    }

    /// <summary>
    /// An address exactly as long as is kept, spelled so that nothing in it can be compressed away on
    /// its way into an index.
    /// </summary>
    private static string LongestAddress(string domain)
    {
        var start = $"https://{domain}/";
        var unrepeated = string.Concat(Enumerable.Range(0, 64).Select(_ => Guid.NewGuid().ToString("n")));

        return start + unrepeated[..(Site.MaxSitemapAddressLength - start.Length)];
    }

    private static string Domain() => $"schema-{Guid.NewGuid():n}.example";

    /// <summary>
    /// A database of a test's own, created by the first migration applied to it and deleted again
    /// afterwards, so that applying an earlier schema touches nothing the other tests read.
    /// </summary>
    /// <param name="purpose">Begins the database's name, so a leftover one says what made it.</param>
    /// <returns>A context over the new database.</returns>
    private ControlPlaneDbContext OwnDatabase(string purpose)
    {
        var connection = new NpgsqlConnectionStringBuilder(stack.ControlPlaneConnectionString)
        {
            Database = $"{purpose}_{Guid.NewGuid():n}",
        };

        return new ControlPlaneDbContext(
            new DbContextOptionsBuilder<ControlPlaneDbContext>()
                .UseNpgsql(connection.ConnectionString, npgsql => npgsql
                    .MigrationsHistoryTable("__ef_migrations_history")
                    .MigrationsAssembly(InfrastructureRegistration.MigrationsAssemblyName))
                .UseSnakeCaseNamingConvention()
                .Options);
    }

    private Task<List<string>> TableNamesAsync() => QueryAsync(TableNamesSql);

    private Task<List<string>> ColumnNamesAsync(string table) => QueryAsync(ColumnNamesSql, table);

    private async Task<List<string>> QueryAsync(string sql, params object[] parameters)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Database.SqlQueryRaw<string>(sql, parameters).ToListAsync(Cancellation.Token);
    }
}
