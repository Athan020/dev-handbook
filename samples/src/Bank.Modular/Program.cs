using Bank.Modular.Ledger;
using Bank.Modular.Payees;
using Bank.Modular.Statements;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);

// #region modules
// Each module registers its own services and maps its own endpoints. The host knows the
// modules exist, and nothing about how they work.
builder.Services.AddLedgerModule(builder.Configuration);
builder.Services.AddPayeesModule(builder.Configuration);
builder.Services.AddStatementsModule(builder.Configuration);

var app = builder.Build();

await LedgerModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);
await PayeesModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);
await StatementsModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);

app.MapLedger();
app.MapPayees();
app.MapStatements();
// #endregion modules

app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program;
