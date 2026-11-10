using AIFramework.Core.Abstractions;
using AIFramework.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace AIFramework.Local;
public static class DependencyInjection
{
    public static IServiceCollection AddLocalAi(this IServiceCollection services)
    {
        services.TryAddSingleton<ITextChunker, FixedSizeTextChunker>();
        services.TryAddSingleton<InMemoryKnowledgeStore>();
        services.AddSingleton<IKnowledgeIndex>(p => p.GetRequiredService<InMemoryKnowledgeStore>());
        services.AddSingleton<IKnowledgeRetriever>(p => p.GetRequiredService<InMemoryKnowledgeStore>());
        services.AddSingleton<IChatModel, LocalGroundedChatModel>();
        services.AddSingleton<ITextGenerationModel, LocalTextGenerationModel>();
        return services;
    }
}
