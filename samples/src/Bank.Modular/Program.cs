using Bank.Modular.Ledger;
using Bank.Modular.Payees;
using Bank.Modular.Statements;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
var layout = ModuleLayout.From(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnavailableModuleHandler>();

// #region modules
// Each module registers its own services and maps its own endpoints. The host knows the
// modules exist, and nothing about how they work. (From stage 3, a module can also run in
// another process; the others then reach it through its contract over HTTP.)
if (layout.Ledger) builder.Services.AddLedgerModule(builder.Configuration);
else builder.Services.AddRemoteLedger(builder.Configuration);

if (layout.Payees) builder.Services.AddPayeesModule(builder.Configuration);
else builder.Services.AddRemotePayees(builder.Configuration);

if (layout.Statements) builder.Services.AddStatementsModule(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();

if (layout.Ledger) await LedgerModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);
if (layout.Payees) await PayeesModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);
if (layout.Statements) await StatementsModule.PrepareAsync(app.Services, app.Lifetime.ApplicationStopping);

if (layout.Ledger) app.MapLedger();
if (layout.Payees) app.MapPayees();
if (layout.Statements) app.MapStatements();
// #endregion modules

app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program;
