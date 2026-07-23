using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedAuctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ranked_auctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerExternalId = table.Column<int>(type: "integer", nullable: false),
                    WindowIndex = table.Column<int>(type: "integer", nullable: false),
                    StartPrice = table.Column<long>(type: "bigint", nullable: false),
                    HighBid = table.Column<long>(type: "bigint", nullable: false),
                    HighBidClubId = table.Column<Guid>(type: "uuid", nullable: true),
                    HighBidClubExternalId = table.Column<int>(type: "integer", nullable: true),
                    HighBidUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EndsUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_auctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_auctions_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_auctions_RankedGroupId_Status",
                table: "ranked_auctions",
                columns: new[] { "RankedGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_auctions_RankedGroupId_WindowIndex",
                table: "ranked_auctions",
                columns: new[] { "RankedGroupId", "WindowIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_auctions");
        }
    }
}
