using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.Payees.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "follow_positions",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_positions", x => x.source);
                });

            migrationBuilder.CreateTable(
                name: "payee_changes",
                columns: table => new
                {
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_account_id = table.Column<int>(type: "integer", nullable: false),
                    payee_account_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payee_changes", x => x.position);
                });

            migrationBuilder.CreateTable(
                name: "payees",
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
                table: "payees",
                columns: new[] { "customer_account_id", "payee_account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "follow_positions");

            migrationBuilder.DropTable(
                name: "payee_changes");

            migrationBuilder.DropTable(
                name: "payees");
        }
    }
}
