using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastMarketWindowOpened",
                table: "ranked_groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeasonStartedUtc",
                table: "ranked_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ranked_fixtures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    MatchIndex = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    KickoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_ranked_fixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_fixtures_clubs_AwayClubId",
                        column: x => x.AwayClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ranked_fixtures_clubs_HomeClubId",
                        column: x => x.HomeClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ranked_fixtures_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ranked_lineups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineupJson = table.Column<string>(type: "text", nullable: false),
                    TacticJson = table.Column<string>(type: "text", nullable: true),
                    PrematchPlanJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_lineups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_lineups_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ranked_lineups_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_fixtures_AwayClubId",
                table: "ranked_fixtures",
                column: "AwayClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_fixtures_HomeClubId",
                table: "ranked_fixtures",
                column: "HomeClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_fixtures_RankedGroupId_IsPlayed",
                table: "ranked_fixtures",
                columns: new[] { "RankedGroupId", "IsPlayed" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_fixtures_RankedGroupId_Round_MatchIndex",
                table: "ranked_fixtures",
                columns: new[] { "RankedGroupId", "Round", "MatchIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_lineups_ClubId",
                table: "ranked_lineups",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_lineups_RankedGroupId_ClubId",
                table: "ranked_lineups",
                columns: new[] { "RankedGroupId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_lineups_RankedGroupId_UserId",
                table: "ranked_lineups",
                columns: new[] { "RankedGroupId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_fixtures");

            migrationBuilder.DropTable(
                name: "ranked_lineups");

            migrationBuilder.DropColumn(
                name: "LastMarketWindowOpened",
                table: "ranked_groups");

            migrationBuilder.DropColumn(
                name: "SeasonStartedUtc",
                table: "ranked_groups");
        }
    }
}
