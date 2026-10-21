using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.Modular.Payees.Migrations
{
    /// <inheritdoc />
    internal partial class PayeeChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "follow_positions",
                schema: "payees",
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
                schema: "payees",
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

            // Payees that existed before the change log: record each as added, so a new
            // follower reading the log from the start gets every one of them.
            migrationBuilder.Sql(PayeesModule.BackfillChanges);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "follow_positions",
                schema: "payees");

            migrationBuilder.DropTable(
                name: "payee_changes",
                schema: "payees");
        }
    }
}
