using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddClassificationReadThrough : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // How far each bookmark already kept has judged is the furthest it is known to have read:
            // how far a bookmark held by an open visit read past that was never recorded, so the first
            // run reads that stretch again and leaves out what it judged. The column is added empty,
            // filled with that, and only then made required.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_through",
                table: "classification_progress",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE classification_progress SET read_through = classified_through;");

            // The default is for a release that does not know the column — one an installation is
            // rolled back to — so that it can still start a bookmark. It lies before anything that
            // can be judged, and a reading behind the judging is read as reaching the judging, so a
            // bookmark started that way reads as having read as far as it has judged.
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "read_through",
                table: "classification_progress",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "'1970-01-01 00:00:00+00'",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "read_through",
                table: "classification_progress");
        }
    }
}
