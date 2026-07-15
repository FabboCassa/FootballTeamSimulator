using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "league_fixtures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    MatchIndex = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPlayed = table.Column<bool>(type: "boolean", nullable: false),
                    HomeGoals = table.Column<int>(type: "integer", nullable: false),
                    AwayGoals = table.Column<int>(type: "integer", nullable: false),
                    MatchSeed = table.Column<long>(type: "bigint", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplayJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_fixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_fixtures_clubs_AwayClubId",
                        column: x => x.AwayClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_league_fixtures_clubs_HomeClubId",
                        column: x => x.HomeClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_league_fixtures_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "league_lineups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineupJson = table.Column<string>(type: "text", nullable: false),
                    TacticJson = table.Column<string>(type: "text", nullable: true),
                    PrematchPlanJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_lineups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_lineups_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_league_lineups_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_league_fixtures_AwayClubId",
                table: "league_fixtures",
                column: "AwayClubId");

            migrationBuilder.CreateIndex(
                name: "IX_league_fixtures_HomeClubId",
                table: "league_fixtures",
                column: "HomeClubId");

            migrationBuilder.CreateIndex(
                name: "IX_league_fixtures_PrivateLeagueId_Round_MatchIndex",
                table: "league_fixtures",
                columns: new[] { "PrivateLeagueId", "Round", "MatchIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_league_lineups_ClubId",
                table: "league_lineups",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_league_lineups_PrivateLeagueId_ClubId",
                table: "league_lineups",
                columns: new[] { "PrivateLeagueId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_league_lineups_PrivateLeagueId_UserId",
                table: "league_lineups",
                columns: new[] { "PrivateLeagueId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "league_fixtures");

            migrationBuilder.DropTable(
                name: "league_lineups");
        }
    }
}
