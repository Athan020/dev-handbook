using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Modular.Statements.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "statements");

            migrationBuilder.CreateTable(
                name: "feed_positions",
                schema: "statements",
                columns: table => new
                {
                    feed = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    last_posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_positions", x => x.feed);
                });

            migrationBuilder.CreateTable(
                name: "lines",
                schema: "statements",
                columns: table => new
                {
                    position = table.Column<long>(type: "bigint", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lines", x => x.position);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lines_account_id_posted_at",
                schema: "statements",
                table: "lines",
                columns: new[] { "account_id", "posted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_positions",
                schema: "statements");

            migrationBuilder.DropTable(
                name: "lines",
                schema: "statements");
        }
    }
}
