using Bank.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Bank.Api.Tests;

/// <summary>Postgres and RabbitMQ in Docker, for B2's tests that publish and consume.</summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3.5-management-alpine")
        .WithUsername("bank").WithPassword("bank").Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string RabbitHost => _rabbit.Hostname;
    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public WebApplicationFactory<Program> App(string transfers) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Bank", ConnectionString)
            .UseSetting("RabbitMQ:Host", RabbitHost)
            .UseSetting("RabbitMQ:Port", RabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("Lab:Transfers", transfers)
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9"));

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        await using var app = App("Inline");
        await using var scope = app.Services.CreateAsyncScope();
        await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<BankDb>(), new Seeder.Options(50, 500, 500), TextWriter.Null);
    }

    public async ValueTask DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
