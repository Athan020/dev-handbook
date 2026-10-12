namespace Assessments.Set3;

// Each question has a visible test on the sample data and a hidden test on the scoring data.
// Rows are compared in order, and every column must match exactly.
public class QueryTests(SqlFixture sql)
{
    [Fact, Trait(Tests.Category, Tests.Visible)]
    public async Task Q1_open_accounts_on_the_sample_data() => Assert.Equal(
    [
        "Durban | 2",
        "Cape Town | 1",
        "Pretoria | 0",
    ], await sql.RunAsync("1-open-accounts", "visible"));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Q1_open_accounts() => Assert.Equal(
    [
        "Durban | 2",
        "Pretoria | 2",
        "Cape Town | 1",
        "Polokwane | 1",
        "Bloemfontein | 0",
        "Gqeberha | 0",
    ], await sql.RunAsync("1-open-accounts", "hidden"));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public async Task Q2_never_received_on_the_sample_data() => Assert.Equal(
        ["4", "5"], await sql.RunAsync("2-never-received", "visible"));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Q2_never_received() => Assert.Equal(
        ["4", "6", "7", "8"], await sql.RunAsync("2-never-received", "hidden"));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public async Task Q3_largest_debit_on_the_sample_data() => Assert.Equal(
    [
        "1 | -500.00 | 2026-01-10 10:00:00",
        "3 | -50.00 | 2026-02-03 12:00:00",
    ], await sql.RunAsync("3-largest-debit", "visible"));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Q3_largest_debit() => Assert.Equal(
    [
        "1 | -1000.00 | 2026-03-02 08:00:00",
        "2 | -0.01 | 2026-05-01 00:00:00",
        "3 | -60.00 | 2026-04-10 13:00:00",
        "7 | -25.50 | 2026-02-28 23:59:59",
    ], await sql.RunAsync("3-largest-debit", "hidden"));

    [Fact, Trait(Tests.Category, Tests.Visible)]
    public async Task Q4_monthly_change_on_the_sample_data() => Assert.Equal(
    [
        "1 | 2026-01-01 | -700.00 | NULL",
        "1 | 2026-02-01 | -500.00 | 200.00",
        "2 | 2026-01-01 | 500.00 | NULL",
        "2 | 2026-02-01 | 550.00 | 50.00",
        "3 | 2026-01-01 | 200.00 | NULL",
        "3 | 2026-02-01 | -50.00 | -250.00",
    ], await sql.RunAsync("4-monthly-change", "visible"));

    [Fact, Trait(Tests.Category, Tests.Hidden)]
    public async Task Q4_monthly_change() => Assert.Equal(
    [
        "1 | 2026-03-01 | -2300.00 | NULL",
        "2 | 2026-03-01 | 1000.00 | NULL",
        "2 | 2026-05-01 | -0.01 | -1000.01",
        "3 | 2026-01-01 | -40.00 | NULL",
        "3 | 2026-04-01 | -60.00 | -20.00",
        "5 | 2026-01-01 | 100.00 | NULL",
        "5 | 2026-04-01 | 60.00 | -40.00",
        "7 | 2026-02-01 | -25.50 | NULL",
        "7 | 2026-03-01 | -25.50 | 0.00",
    ], await sql.RunAsync("4-monthly-change", "hidden"));
}
