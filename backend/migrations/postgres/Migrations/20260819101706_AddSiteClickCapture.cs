using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteClickCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // True, so that a site registered before this upgrade behaves exactly like one
            // registered after it. Two sites on one install differing in what they collect,
            // because of when each happened to be added, is the kind of difference nobody
            // would ever think to look for.
            migrationBuilder.AddColumn<bool>(
                name: "capture_clicks",
                table: "sites",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capture_clicks",
                table: "sites");
        }
    }
}
