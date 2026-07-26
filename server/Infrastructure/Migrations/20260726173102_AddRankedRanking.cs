using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedRanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SeasonEndedUtc",
                table: "ranked_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonNumber",
                table: "ranked_groups",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PeakRating",
                table: "ranked_coaches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SeasonsPlayed",
                table: "ranked_coaches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ranked_awards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RankedWorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    GroupName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    RatingAfter = table.Column<int>(type: "integer", nullable: false),
                    RatingDelta = table.Column<int>(type: "integer", nullable: false),
                    AwardedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_awards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_awards_UserId_AwardedUtc",
                table: "ranked_awards",
                columns: new[] { "UserId", "AwardedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_awards_UserId_Kind",
                table: "ranked_awards",
                columns: new[] { "UserId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_awards");

            migrationBuilder.DropColumn(
                name: "SeasonEndedUtc",
                table: "ranked_groups");

            migrationBuilder.DropColumn(
                name: "SeasonNumber",
                table: "ranked_groups");

            migrationBuilder.DropColumn(
                name: "PeakRating",
                table: "ranked_coaches");

            migrationBuilder.DropColumn(
                name: "SeasonsPlayed",
                table: "ranked_coaches");
        }
    }
}
