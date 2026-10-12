// The worker for journey B2: takes follow-up work for transfers off a RabbitMQ queue and
// does it (an email and a fraud score). Run as many copies as you like; RabbitMQ shares
// the messages between them.
using Bank.Messaging;
using Bank.Worker;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient<FollowUp>(http =>
    http.BaseAddress = new Uri(builder.Configuration["FakeServices:Url"] ?? "http://localhost:5090"));
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Bank")!));
builder.Services.AddSingleton<Rewards>();
builder.Services.AddSingleton<Statements>();
// The statements service has its own consumer (stage 5); every other role uses Consumer.
if (builder.Configuration["Worker:Role"] == "statements")
{
    builder.Services.AddHostedService<StatementsConsumer>();
}
else
{
    builder.Services.AddHostedService<Consumer>();
}
builder.Build().Run();
