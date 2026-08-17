using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Target = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Details = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "balance_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    RolledBackFrom = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balance_revisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_Action_CreatedUtc",
                table: "admin_audit",
                columns: new[] { "Action", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_ActorUserId",
                table: "admin_audit",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_CreatedUtc",
                table: "admin_audit",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_balance_revisions_Revision",
                table: "balance_revisions",
                column: "Revision",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit");

            migrationBuilder.DropTable(
                name: "balance_revisions");
        }
    }
}
