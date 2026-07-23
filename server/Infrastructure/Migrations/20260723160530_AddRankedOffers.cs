using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ranked_offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowIndex = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerExternalId = table.Column<int>(type: "integer", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranked_offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ranked_offers_ranked_groups_RankedGroupId",
                        column: x => x.RankedGroupId,
                        principalTable: "ranked_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_offers_BuyerUserId",
                table: "ranked_offers",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ranked_offers_RankedGroupId_Status",
                table: "ranked_offers",
                columns: new[] { "RankedGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_offers_SellerUserId",
                table: "ranked_offers",
                column: "SellerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranked_offers");
        }
    }
}
