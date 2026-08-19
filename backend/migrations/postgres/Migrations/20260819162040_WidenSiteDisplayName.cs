using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class WidenSiteDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "sites",
                type: "character varying(253)",
                maxLength: 253,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "sites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(253)",
                oldMaxLength: 253);
        }
    }
}
