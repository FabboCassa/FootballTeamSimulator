using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedDailyLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmedRound",
                table: "ranked_lineups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ranked_trainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_trainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_trainings_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ranked_trainings_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_trainings_ClubId",
                table: "ranked_trainings",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_trainings_RankedGroupId_ClubId",
                table: "ranked_trainings",
                columns: new[] { "RankedGroupId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_trainings_RankedGroupId_UserId",
                table: "ranked_trainings",
                columns: new[] { "RankedGroupId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_trainings");

            migrationBuilder.DropColumn(
                name: "ConfirmedRound",
                table: "ranked_lineups");
        }
    }
}
