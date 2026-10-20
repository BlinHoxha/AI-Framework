using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
namespace AIFramework.IntegrationTests.Infrastructure;
public sealed class FrameworkApiFactory : WebApplicationFactory<AIFramework.SampleApi.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("AI:AllowAnonymousDevelopment", "true");
    }
}