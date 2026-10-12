// The worker for journey B2: takes follow-up work for transfers off a RabbitMQ queue and
// does it (an email and a fraud score). Run as many copies as you like; RabbitMQ shares
// the messages between them.
using Bank.Messaging;
using Bank.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient<FollowUp>(http =>
    http.BaseAddress = new Uri(builder.Configuration["FakeServices:Url"] ?? "http://localhost:5090"));
builder.Services.AddHostedService<FollowUpConsumer>();
builder.Build().Run();
