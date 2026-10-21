using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

// B5 stage 6: one front door for the bank's services. The gateway routes each request to the
// service that owns it (appsettings.json), checks the customer's token once, limits each
// customer's rate, and passes the verified identity on. It also hosts the mobile app's
// backend-for-frontend endpoint, GET /app/home. It holds no business rules.

var builder = WebApplication.CreateBuilder(args);
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Gateway:SigningKey"]!));

// #region edge
// Authentication at the edge: a request without a valid token never reaches a service.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = Lab.Issuer,
        ValidAudience = Lab.Audience,
        IssuerSigningKey = signingKey,
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("customer", policy => policy.RequireAuthenticatedUser().RequireClaim(Lab.AccountClaim));

// A rate limit per customer, before any service does any work (B4 stage 6).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-customer", context => RateLimitPartition.GetTokenBucketLimiter(
        context.User.FindFirstValue(Lab.AccountClaim) ?? "anonymous",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = builder.Configuration.GetValue("Gateway:Burst", 40),
            TokensPerPeriod = builder.Configuration.GetValue("Gateway:RatePerSecond", 20),
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        }));
});

// Routing, from configuration, with one transform: the services receive the identity the
// gateway verified, and never a value a client sent.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms => transforms.AddRequestTransform(context =>
    {
        context.ProxyRequest.Headers.Remove(Lab.CustomerHeader);
        if (context.HttpContext.User.FindFirstValue(Lab.AccountClaim) is { } account)
            context.ProxyRequest.Headers.Add(Lab.CustomerHeader, account);
        return ValueTask.CompletedTask;
    }));
// #endregion edge

builder.Services.AddHome(builder.Configuration);
builder.Services.AddPayeesMigration();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// #region ownership
app.MapReverseProxy(proxy =>
{
    // A customer may only reach their own account's routes. The gateway can check this much,
    // because the account is in the path; whose payee or transfer an id refers to, only the
    // service that owns it can say.
    proxy.Use(async (context, next) =>
    {
        if (context.GetRouteValue("accountId") is string accountId
            && accountId != context.User.FindFirstValue(Lab.AccountClaim))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next();
    });

    // Stage 7: payees requests go to the monolith's module or the new service, as the
    // migration says.
    proxy.Use((context, next) => context.GetReverseProxyFeature().Route.Config.ClusterId == "payees"
        ? context.RequestServices.GetRequiredService<PayeesMigration>().RouteAsync(context, next)
        : next());
});
// #endregion ownership

app.MapHome();
app.MapPayeesMigration();

// Lab only: a token for any account you ask for. A real system gets tokens from an identity
// provider, after the customer has signed in (B9).
app.MapPost("/lab/token", (int accountId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
{
    Issuer = Lab.Issuer,
    Audience = Lab.Audience,
    Claims = new Dictionary<string, object> { [Lab.AccountClaim] = accountId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
    Expires = DateTime.UtcNow.AddHours(1),
    SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
}));

app.MapGet("/health", () => Results.Ok());

app.Run();

static class Lab
{
    public const string Issuer = "bank-lab";
    public const string Audience = "bank-app";
    public const string AccountClaim = "account_id";
    public const string CustomerHeader = "X-Customer-Account";
}

public partial class Program;
