using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Modular.Payees.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payees");

            migrationBuilder.CreateTable(
                name: "payees",
                schema: "payees",
                columns: table => new
                {
                    payee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_account_id = table.Column<int>(type: "integer", nullable: false),
                    payee_account_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payees", x => x.payee_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payees_customer_account_id_payee_account_id",
                schema: "payees",
                table: "payees",
                columns: new[] { "customer_account_id", "payee_account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payees",
                schema: "payees");
        }
    }
}
