using System.Text;
using System.Text.Json;
using CedarClerk.Core;

namespace CedarClerk.Server.Translation;

public class DeepLTranslationProvider(IHttpClientFactory httpFactory, string apiKey) : ITranslationProvider
{
    public string Name => "deepl";

    public async Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct)
    {
        List<string> texts;
        try
        {
            texts = TipTapTextNodes.ExtractTexts(cedarJson);
        }
        catch (Exception ex)
        {
            throw new TranslationException("Draft document is not valid JSON", ex);
        }

        var batch = new List<string> { title };
        batch.AddRange(texts);

        var host = apiKey.EndsWith(":fx") ? "api-free.deepl.com" : "api.deepl.com";
        var http = httpFactory.CreateClient("translation");
        http.Timeout = TimeSpan.FromMinutes(2);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/v2/translate");
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { text = batch, target_lang = targetLanguage.ToUpperInvariant() }),
            Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new TranslationException($"DeepL API request failed: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new TranslationException($"DeepL API returned {(int)response.StatusCode}: {(json.Length <= 300 ? json : json[..300])}");

        List<string> translated;
        try
        {
            using var doc = JsonDocument.Parse(json);
            translated = doc.RootElement.GetProperty("translations")
                .EnumerateArray()
                .Select(t => t.GetProperty("text").GetString() ?? "")
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranslationException("DeepL returned an unexpected response shape", ex);
        }

        if (translated.Count != batch.Count)
            throw new TranslationException("DeepL returned a different number of translations than requested");

        var translatedTitle = translated[0];
        var translatedJson = TipTapTextNodes.ReplaceTexts(cedarJson, translated.Skip(1).ToList());
        return new TranslationResult(translatedTitle, translatedJson);
    }
}
