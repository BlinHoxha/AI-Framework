using AIFramework.Application;
using AIFramework.Azure;
using AIFramework.Aws;
using AIFramework.Local;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddJsonConsole();
builder.Services.AddAiApplication();
var provider = builder.Configuration["AI:Provider"] ?? "Local";
switch (provider)
{
    case "Local": builder.Services.AddLocalAi(); break;
    case "Azure": builder.Services.AddAzureAi(builder.Configuration); break;
    case "Aws":
        // Sample only: local retrieval is ephemeral; production must supply a durable retriever/index.
        builder.Services.AddLocalAi();
        builder.Services.AddBedrockChat(new(builder.Configuration["AI:Aws:Region"] ?? "",
            builder.Configuration["AI:Aws:ModelId"] ?? ""));
        break;
    default: throw new InvalidOperationException("Unsupported AI provider.");
}
bool anonymousDemo = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("AI:AllowAnonymousDevelopment");
var authority = builder.Configuration["Authentication:Authority"];
var audience = builder.Configuration["Authentication:Audience"];
if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience)))
    throw new InvalidOperationException("Authentication authority and audience are required outside Development.");
if (!builder.Environment.IsDevelopment() && provider is "Local" or "Aws")
    throw new InvalidOperationException("The sample's ephemeral Local/Aws retrieval is Development-only; compose a durable production host.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    if (!string.IsNullOrWhiteSpace(authority)) o.Authority = authority;
    if (!string.IsNullOrWhiteSpace(audience)) o.Audience = audience;
});
builder.Services.AddAuthorization(o =>
{
    if (!anonymousDemo) o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async http =>
{
    var error = http.Features.Get<IExceptionHandlerFeature>()?.Error;
    http.Response.StatusCode = error is ArgumentException ? 400 : error is KeyNotFoundException ? 404 : 500;
    await Results.Problem(statusCode: http.Response.StatusCode, title: http.Response.StatusCode < 500 ? error?.Message : "AI processing failed.").ExecuteAsync(http);
}));
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.Run();
namespace AIFramework.SampleApi { public partial class Program; }