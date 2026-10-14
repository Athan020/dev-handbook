using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outgoing_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    to_bank = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    to_account = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_payments_created_at",
                table: "outgoing_payments",
                column: "created_at",
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_payments");
        }
    }
}
