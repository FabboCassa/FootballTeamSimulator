using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedSellerLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SellerClubExternalId",
                table: "ranked_auctions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerClubId",
                table: "ranked_auctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerUserId",
                table: "ranked_auctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranked_auctions_RankedGroupId_SellerClubId",
                table: "ranked_auctions",
                columns: new[] { "RankedGroupId", "SellerClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_ranked_auctions_Status_EndsUtc",
                table: "ranked_auctions",
                columns: new[] { "Status", "EndsUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ranked_auctions_RankedGroupId_SellerClubId",
                table: "ranked_auctions");

            migrationBuilder.DropIndex(
                name: "IX_ranked_auctions_Status_EndsUtc",
                table: "ranked_auctions");

            migrationBuilder.DropColumn(
                name: "SellerClubExternalId",
                table: "ranked_auctions");

            migrationBuilder.DropColumn(
                name: "SellerClubId",
                table: "ranked_auctions");

            migrationBuilder.DropColumn(
                name: "SellerUserId",
                table: "ranked_auctions");
        }
    }
}
