using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedWorlds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ranked_worlds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_worlds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ranked_coaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedWorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlacementGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlacementPosition = table.Column<int>(type: "integer", nullable: true),
                    AutoEnrol = table.Column<bool>(type: "boolean", nullable: false),
                    EnrolledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlacedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_coaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_coaches_ranked_worlds_RankedWorldId",
                        column: x => x.RankedWorldId,
                        principalTable: "ranked_worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ranked_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedWorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    GroupIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    WorldId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_groups_ranked_worlds_RankedWorldId",
                        column: x => x.RankedWorldId,
                        principalTable: "ranked_worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ranked_groups_worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ranked_seats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatIndex = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccupiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_seats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_seats_clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ranked_seats_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_coaches_RankedWorldId_Status",
                table: "ranked_coaches",
                columns: new[] { "RankedWorldId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_coaches_UserId",
                table: "ranked_coaches",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_groups_Kind_Status",
                table: "ranked_groups",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_groups_RankedWorldId_Kind_Tier_GroupIndex",
                table: "ranked_groups",
                columns: new[] { "RankedWorldId", "Kind", "Tier", "GroupIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_groups_WorldId",
                table: "ranked_groups",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_seats_ClubId",
                table: "ranked_seats",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_seats_RankedGroupId_SeatIndex",
                table: "ranked_seats",
                columns: new[] { "RankedGroupId", "SeatIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_seats_UserId",
                table: "ranked_seats",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_worlds_Status",
                table: "ranked_worlds",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_coaches");

            migrationBuilder.DropTable(
                name: "ranked_seats");

            migrationBuilder.DropTable(
                name: "ranked_groups");

            migrationBuilder.DropTable(
                name: "ranked_worlds");
        }
    }
}
