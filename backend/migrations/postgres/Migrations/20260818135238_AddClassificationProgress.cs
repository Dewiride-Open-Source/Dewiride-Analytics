using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddClassificationProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classification_progress",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruleset_major = table.Column<int>(type: "integer", nullable: false),
                    ruleset_minor = table.Column<int>(type: "integer", nullable: false),
                    classified_through = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_progress", x => new { x.site_id, x.ruleset_major, x.ruleset_minor });
                    table.ForeignKey(
                        name: "fk_classification_progress_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classification_progress");
        }
    }
}
