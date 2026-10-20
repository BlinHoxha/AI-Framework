using AIFramework.Core.Abstractions;
using AIFramework.Core.Processing;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace AIFramework.Azure;
public static class DependencyInjection
{
    public static IServiceCollection AddAzureAi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AzureAiOptions.SectionName);
        var options = AzureAiOptions.FromValues(section["OpenAiEndpoint"], section["ChatDeployment"],
            section["EmbeddingDeployment"], section["SearchEndpoint"], section["SearchIndex"]);
        services.AddAzureModels(new(options.OpenAiEndpoint, options.ChatDeployment, options.EmbeddingDeployment));
        services.AddAzureSearch(new(options.SearchEndpoint, options.SearchIndex));
        return services;
    }

    private static void AddTransport(IServiceCollection services)
    {
        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddHttpClient<AzureTokenClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
    }

    public static IServiceCollection AddAzureModels(this IServiceCollection services, AzureModelOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        AddTransport(services);
        services.TryAddSingleton<ITextChunker, FixedSizeTextChunker>();
        services.AddSingleton<AzureOpenAiClient>(p => new AzureOpenAiClient(options, p.GetRequiredService<AzureTokenClient>()));
        services.AddSingleton<IChatModel>(p => p.GetRequiredService<AzureOpenAiClient>());
        services.AddSingleton<ITextEmbeddingModel>(p => p.GetRequiredService<AzureOpenAiClient>());
        return services;
    }

    // A host may supply any compatible embedding model; chat provider selection is independent.
    public static IServiceCollection AddAzureSearch(this IServiceCollection services, AzureSearchOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        AddTransport(services);
        services.AddSingleton<AzureSearchKnowledgeStore>(p => new AzureSearchKnowledgeStore(options,
            p.GetRequiredService<AzureTokenClient>(), p.GetRequiredService<ITextEmbeddingModel>()));
        services.AddSingleton<IKnowledgeIndex>(p => p.GetRequiredService<AzureSearchKnowledgeStore>());
        services.AddSingleton<IKnowledgeRetriever>(p => p.GetRequiredService<AzureSearchKnowledgeStore>());
        return services;
    }
}
