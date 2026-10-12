using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderedAccountEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "exchange",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "transfers");

            migrationBuilder.AddColumn<long>(
                name: "position",
                table: "outbox_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.AddColumn<string>(
                name: "routing_key",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "transfer.completed");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "statement_accounts",
                columns: table => new
                {
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    last_sequence = table.Column<int>(type: "integer", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_accounts", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "statement_problems",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    problem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_problems", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_position",
                table: "outbox_messages",
                column: "position",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statement_accounts");

            migrationBuilder.DropTable(
                name: "statement_problems");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_position",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "exchange",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "position",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "routing_key",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "version",
                table: "accounts");
        }
    }
}
