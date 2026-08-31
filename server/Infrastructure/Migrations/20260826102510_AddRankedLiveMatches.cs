using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedLiveMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "ranked_worlds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LiveKickoffNotifiedRound",
                table: "ranked_groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ranked_live_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FixtureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    HomeClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwayUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HomePresent = table.Column<bool>(type: "boolean", nullable: false),
                    AwayPresent = table.Column<bool>(type: "boolean", nullable: false),
                    KickoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangesJson = table.Column<string>(type: "text", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: true),
                    HomeGoals = table.Column<int>(type: "integer", nullable: false),
                    AwayGoals = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_live_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_live_matches_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_live_matches_FixtureId",
                table: "ranked_live_matches",
                column: "FixtureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_live_matches_RankedGroupId_Round_Status",
                table: "ranked_live_matches",
                columns: new[] { "RankedGroupId", "Round", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_live_matches");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "ranked_worlds");

            migrationBuilder.DropColumn(
                name: "LiveKickoffNotifiedRound",
                table: "ranked_groups");
        }
    }
}
