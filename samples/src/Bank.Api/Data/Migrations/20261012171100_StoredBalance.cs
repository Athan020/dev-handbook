using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoredBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "balance",
                table: "accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "balance_version",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing lab databases: start each stored balance at the sum of the account's
            // transactions (the same SQL as Data/Seed/balances.sql). Empty databases skip it.
            migrationBuilder.Sql("""
                UPDATE accounts a
                SET balance = t.total
                FROM (SELECT account_id, sum(amount) AS total FROM transactions GROUP BY account_id) t
                WHERE a.id = t.account_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "balance",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "balance_version",
                table: "accounts");
        }
    }
}
