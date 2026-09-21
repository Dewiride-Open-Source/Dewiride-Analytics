using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_invitations_users_invited_by_user_id",
                table: "organization_invitations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by_user_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_reminder_sent_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "invited_by_user_id",
                table: "organization_invitations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "installation_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_claims", x => x.id);
                    table.CheckConstraint("ck_installation_claims_one_row", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_organizations_closed_at",
                table: "organizations",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_closed_by_user_id",
                table: "organizations",
                column: "closed_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_organization_invitations_users_invited_by_user_id",
                table: "organization_invitations",
                column: "invited_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_organizations_users_closed_by_user_id",
                table: "organizations",
                column: "closed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // An install that already has accounts was claimed before this row existed to say so.
            // Recording the claim now, dated from its earliest account, is what keeps the welcome
            // screen shut on the day those accounts are deleted with a closed organisation; without
            // it an install emptied that way would look exactly as it did before anybody claimed it.
            migrationBuilder.Sql(
                """
                INSERT INTO installation_claims (id, claimed_at)
                SELECT 1, MIN(created_at)
                FROM users
                HAVING COUNT(*) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_invitations_users_invited_by_user_id",
                table: "organization_invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_organizations_users_closed_by_user_id",
                table: "organizations");

            migrationBuilder.DropTable(
                name: "installation_claims");

            migrationBuilder.DropIndex(
                name: "ix_organizations_closed_at",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_organizations_closed_by_user_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "closed_by_user_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "deletion_reminder_sent_at",
                table: "organizations");

            migrationBuilder.AlterColumn<Guid>(
                name: "invited_by_user_id",
                table: "organization_invitations",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_organization_invitations_users_invited_by_user_id",
                table: "organization_invitations",
                column: "invited_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
