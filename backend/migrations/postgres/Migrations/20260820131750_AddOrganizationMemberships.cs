using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dewiride.Analytics.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_memberships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_memberships_user_id",
                table: "organization_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_organization_memberships_organization_user",
                table: "organization_memberships",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            // Existing installs already have people on sites but nothing recording where they
            // stand in the account that owns them, and there is no upgrade window in which anyone
            // could be asked. Each person is given the widest standing their existing grants
            // imply, so that a scope resolved from this table matches the one resolved from the
            // grants it was derived from rather than quietly narrowing on the day it ships.
            migrationBuilder.Sql(
                """
                INSERT INTO organization_memberships (id, organization_id, user_id, role, granted_at)
                SELECT
                    gen_random_uuid(),
                    grants.organization_id,
                    grants.user_id,
                    CASE MAX(grants.standing)
                        WHEN 3 THEN 'Owner'
                        WHEN 2 THEN 'Admin'
                        ELSE 'Member'
                    END,
                    MIN(grants.granted_at)
                FROM (
                    SELECT
                        sites.organization_id,
                        site_memberships.user_id,
                        site_memberships.granted_at,
                        CASE site_memberships.role
                            WHEN 'Owner' THEN 3
                            WHEN 'Editor' THEN 2
                            ELSE 1
                        END AS standing
                    FROM site_memberships
                    JOIN sites ON sites.id = site_memberships.site_id
                ) AS grants
                GROUP BY grants.organization_id, grants.user_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_memberships");
        }
    }
}
