using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;

namespace AIFramework.Azure;

public sealed class AzureTokenClient(HttpClient httpClient, TokenCredential credential)
{
    public async Task<JsonDocument> PostAsync(Uri uri, object body, string scope, CancellationToken cancellationToken)
        => await SendAsync(uri, body, scope, null, cancellationToken);

    public Task<JsonDocument> PostBoundedAsync(Uri uri, object body, string scope, int maxResponseBytes, CancellationToken cancellationToken)
    {
        if (maxResponseBytes is < 1 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        return SendAsync(uri, body, scope, maxResponseBytes, cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(Uri uri, object body, string scope, int? maxResponseBytes, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using HttpResponseMessage response = await httpClient.SendAsync(request,
            maxResponseBytes is null ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (maxResponseBytes is { } limit)
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > limit) throw new AIFramework.Core.Models.StructuredGenerationException("response_too_large");
                buffer.Write(chunk, 0, read);
            }
            return JsonDocument.Parse(buffer.ToArray());
        }
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
