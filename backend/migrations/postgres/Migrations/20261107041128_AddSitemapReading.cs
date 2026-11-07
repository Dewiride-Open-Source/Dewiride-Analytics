using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSitemapReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Empty on every website already here, and nothing is read for one until somebody names a
            // sitemap. The three tables below start empty for the same reason, so there is nothing to
            // fill in on anybody's existing data.
            migrationBuilder.AddColumn<string>(
                name: "sitemap_address",
                table: "sites",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sitemap_parts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    is_posts = table.Column<bool>(type: "boolean", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: true),
                    etag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    entries = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    off_site_entries = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    query_entries = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sitemap_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_sitemap_parts_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sitemap_reads",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_pass_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_cycle_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cycle_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    problem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    root_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    root_etag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    root_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    root_problem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sitemap_reads", x => x.site_id);
                    table.ForeignKey(
                        name: "fk_sitemap_reads_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_posts",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    address = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    landed_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    part_id = table.Column<Guid>(type: "uuid", nullable: true),
                    part_generation = table.Column<Guid>(type: "uuid", nullable: true),
                    listed_in_posts_part = table.Column<bool>(type: "boolean", nullable: false),
                    listed_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_listed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unlisted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    page_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    is_post = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "page_type IS DISTINCT FROM 'Other' AND page_outcome IS DISTINCT FROM 'Gone' AND (listed_in_posts_part OR page_type IS NOT DISTINCT FROM 'Article')", stored: true),
                    page_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    page_attempts = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    page_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    page_outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    page_read_lastmod = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    page_etag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    page_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_posts", x => new { x.site_id, x.path });
                    table.ForeignKey(
                        name: "fk_site_posts_sitemap_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "sitemap_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_site_posts_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_posts_page_due",
                table: "site_posts",
                columns: new[] { "site_id", "page_due_at" },
                filter: "page_due_at IS NOT NULL AND unlisted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_site_posts_part_id",
                table: "site_posts",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ux_sitemap_parts_site_address",
                table: "sitemap_parts",
                columns: new[] { "site_id", "address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sitemap_reads_host_claimed_until",
                table: "sitemap_reads",
                columns: new[] { "host", "claimed_until" });

            migrationBuilder.CreateIndex(
                name: "ix_sitemap_reads_next_pass_at",
                table: "sitemap_reads",
                column: "next_pass_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_posts");

            migrationBuilder.DropTable(
                name: "sitemap_reads");

            migrationBuilder.DropTable(
                name: "sitemap_parts");

            migrationBuilder.DropColumn(
                name: "sitemap_address",
                table: "sites");
        }
    }
}
