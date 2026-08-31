using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "league_listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerExternalId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubExternalId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AskingPrice = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_listings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_listings_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "league_offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateLeagueId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowIndex = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerExternalId = table.Column<int>(type: "integer", nullable: false),
                    BuyerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerClubExternalId = table.Column<int>(type: "integer", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerClubExternalId = table.Column<int>(type: "integer", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    ProposedBy = table.Column<int>(type: "integer", nullable: false),
                    Rounds = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_league_offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_league_offers_private_leagues_PrivateLeagueId",
                        column: x => x.PrivateLeagueId,
                        principalTable: "private_leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_league_listings_PrivateLeagueId_ClubId",
                table: "league_listings",
                columns: new[] { "PrivateLeagueId", "ClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_league_listings_PrivateLeagueId_PlayerId",
                table: "league_listings",
                columns: new[] { "PrivateLeagueId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_league_offers_BuyerUserId",
                table: "league_offers",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_league_offers_PrivateLeagueId_PlayerId",
                table: "league_offers",
                columns: new[] { "PrivateLeagueId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_league_offers_PrivateLeagueId_Status",
                table: "league_offers",
                columns: new[] { "PrivateLeagueId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_league_offers_SellerUserId",
                table: "league_offers",
                column: "SellerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "league_listings");

            migrationBuilder.DropTable(
                name: "league_offers");
        }
    }
}
