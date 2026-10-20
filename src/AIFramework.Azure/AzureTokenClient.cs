using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;

namespace AIFramework.Azure;

public sealed class AzureTokenClient(HttpClient httpClient, TokenCredential credential)
{
    public async Task<JsonDocument> PostAsync(Uri uri, object body, string scope, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
