using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Transl8r.Translation;

/// <summary>
/// DeepL API translator. Uses header-based auth (DeepL deprecated the form-body
/// auth_key method in Nov 2025 → 403). Free keys end in ":fx" and use
/// api-free.deepl.com; Pro keys use api.deepl.com — selected automatically.
/// </summary>
internal sealed class DeepLTranslator : ITranslator
{
    private readonly string _url;
    private readonly string _key;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public DeepLTranslator(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("DeepL selected but no API key configured.");
        }
        _key = apiKey.Trim();
        _url = _key.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";
    }

    public string Translate(string text)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["source_lang"] = "JA",
            ["target_lang"] = "EN-US",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, _url) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _key);

        using HttpResponseMessage resp = _http.Send(req);
        string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!resp.IsSuccessStatusCode)
        {
            string msg = body;
            try
            {
                using var err = JsonDocument.Parse(body);
                if (err.RootElement.TryGetProperty("message", out var m))
                {
                    msg = m.GetString() ?? body;
                }
            }
            catch (JsonException)
            {
                // non-JSON error body — use it as-is
            }
            throw new InvalidOperationException($"DeepL {(int)resp.StatusCode}: {msg}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("translations")[0]
            .GetProperty("text").GetString() ?? "";
    }
}
