using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Fitness",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "Form",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "Morale",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.CreateTable(
                name: "league_trainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_trainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_trainings_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_league_trainings_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_league_trainings_ClubId",
                table: "league_trainings",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_league_trainings_PrivateLeagueId_ClubId",
                table: "league_trainings",
                columns: new[] { "PrivateLeagueId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_league_trainings_PrivateLeagueId_UserId",
                table: "league_trainings",
                columns: new[] { "PrivateLeagueId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "league_trainings");

            migrationBuilder.DropColumn(
                name: "Fitness",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Form",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Morale",
                table: "players");
        }
    }
}
