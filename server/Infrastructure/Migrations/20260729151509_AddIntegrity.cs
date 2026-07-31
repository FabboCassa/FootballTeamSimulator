using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_signals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_signals_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integrity_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RankedGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayerExternalId = table.Column<int>(type: "integer", nullable: false),
                    Fee = table.Column<long>(type: "bigint", nullable: false),
                    MarketValue = table.Column<long>(type: "bigint", nullable: false),
                    Details = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integrity_flags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_signals_AddressHash",
                table: "account_signals",
                column: "AddressHash");

            migrationBuilder.CreateIndex(
                name: "IX_account_signals_DeviceHash",
                table: "account_signals",
                column: "DeviceHash");

            migrationBuilder.CreateIndex(
                name: "IX_account_signals_UserId_AddressHash_DeviceHash",
                table: "account_signals",
                columns: new[] { "UserId", "AddressHash", "DeviceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrity_flags_Kind_CreatedUtc",
                table: "integrity_flags",
                columns: new[] { "Kind", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_integrity_flags_Status_CreatedUtc",
                table: "integrity_flags",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_integrity_flags_SubjectUserId",
                table: "integrity_flags",
                column: "SubjectUserId");

            migrationBuilder.CreateIndex(
                name: "IX_integrity_flags_UserId",
                table: "integrity_flags",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_signals");

            migrationBuilder.DropTable(
                name: "integrity_flags");
        }
    }
}
