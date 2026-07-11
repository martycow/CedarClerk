using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CedarClerk.Server.Translation;

public class OpenAiTranslationProvider(IHttpClientFactory httpFactory, string apiKey, string model) : ITranslationProvider
{
    public string Name => "openai";
    private const string Url = "https://api.openai.com/v1/chat/completions";

    public async Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct)
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
                    content = TranslationPromptGenerator.Build(title, cedarJson, targetLanguage)
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
            throw new TranslationException($"OpenAI API request failed: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new TranslationException($"OpenAI API returned {(int)response.StatusCode}: {Truncate(json)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return TranslationPromptGenerator.ParseResult(content);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new TranslationException("OpenAI returned an unexpected response shape", ex);
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
