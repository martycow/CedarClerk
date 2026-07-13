using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CedarClerk.Server.Ai;

public class OpenAiAiEditProvider(IHttpClientFactory httpFactory, string apiKey, string model) : IAiEditProvider
{
    public string Name => "openai";
    private const string Url = "https://api.openai.com/v1/chat/completions";

    public async Task<AiEditResult> EditAsync(string title, string cedarJson, AiEditKind kind, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("translation");
        http.Timeout = TimeSpan.FromMinutes(5);

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = AiEditPromptGenerator.Build(title, cedarJson, kind)
                }
            },
            response_format = new
            {
                type = "json_object"
            },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new AiEditException($"OpenAI API request failed: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new AiEditException($"OpenAI API returned {(int)response.StatusCode}: {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return AiEditPromptGenerator.ParseResult(content);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiEditException("OpenAI returned an unexpected response shape", ex);
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
