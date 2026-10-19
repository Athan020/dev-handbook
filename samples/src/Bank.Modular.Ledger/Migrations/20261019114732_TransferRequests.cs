using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Modular.Ledger.Migrations
{
    /// <inheritdoc />
    internal partial class TransferRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_requests",
                schema: "ledger",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<int>(type: "integer", nullable: false),
                    payee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_requests", x => x.transfer_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_transfer_id",
                schema: "ledger",
                table: "entries",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_requests_status_next_attempt_at",
                schema: "ledger",
                table: "transfer_requests",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_requests",
                schema: "ledger");

            migrationBuilder.DropIndex(
                name: "ix_entries_transfer_id",
                schema: "ledger",
                table: "entries");
        }
    }
}
