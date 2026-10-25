using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class StopRecordingClicksUntilAsked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every website starts this release with recording what people press switched off, as a
            // website added from now on does: on a page that shows visitors their own messages, names
            // or accounts, the words on its buttons are theirs, and only somebody who knows the site
            // knows which kind of page it has. Whoever runs a website switches it back on there.
            migrationBuilder.Sql("UPDATE sites SET capture_clicks = false WHERE capture_clicks;");

            // The column's own default is false as well. The product always says which it wants, but a
            // row written by anything that does not — a script, a restore, an insert by hand — starts
            // with recording off, as a website added through the product does.
            migrationBuilder.Sql("ALTER TABLE sites ALTER COLUMN capture_clicks SET DEFAULT false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which websites were recording before is not kept anywhere, so only the column's default
            // is put back.
            migrationBuilder.Sql("ALTER TABLE sites ALTER COLUMN capture_clicks SET DEFAULT true;");
        }
    }
}
