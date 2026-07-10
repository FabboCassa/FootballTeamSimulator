using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "private_leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InviteCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_private_leagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_private_leagues_worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "league_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_members_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_league_members_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_league_members_ClubId",
                table: "league_members",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_league_members_PrivateLeagueId_UserId",
                table: "league_members",
                columns: new[] { "PrivateLeagueId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_league_members_UserId",
                table: "league_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_private_leagues_CreatorUserId",
                table: "private_leagues",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_private_leagues_InviteCode",
                table: "private_leagues",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_private_leagues_WorldId",
                table: "private_leagues",
                column: "WorldId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "league_members");

            migrationBuilder.DropTable(
                name: "private_leagues");
        }
    }
}
