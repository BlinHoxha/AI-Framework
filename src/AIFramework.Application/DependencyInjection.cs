using AIFramework.Application.DocumentProfiles;
using AIFramework.Application.Services;
using AIFramework.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace AIFramework.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddAiApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IDocumentProfileCatalog, DefaultDocumentProfileCatalog>();
        services.TryAddScoped<DocumentIngestionService>();
        services.TryAddScoped<KnowledgeAnswerService>();
        return services;
    }
}