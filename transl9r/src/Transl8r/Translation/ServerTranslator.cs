using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Transl8r.Net;

namespace Transl8r.Translation;

/// <summary>OpenAI-compatible chat endpoint (llama.cpp server, Ollama, vLLM…).
/// Uses the IPv4-preferring client since this often points at localhost.</summary>
internal sealed class ServerTranslator : ITranslator
{
    private const string TargetLanguage = "English";

    private readonly string _url;
    private readonly string _model;
    private readonly HttpClient _http;

    public ServerTranslator(string baseUrl, string model)
    {
        _url = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        _model = model;
        _http = IPv4HttpClient.Create(TimeSpan.FromSeconds(60));
    }

    // Hy-MT2's documented translation template: no system prompt, the instruction
    // lives inline in the user turn. Generic OpenAI-compatible instruct models
    // follow this single-message form just as well, so it stays portable.
    private static string BuildPrompt(string text) =>
        $"Translate the following text into {TargetLanguage}. Note that you should " +
        "**only output the translated result without any additional explanation**:\n\n" +
        text;

    public string Translate(string text)
    {
        var payload = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = BuildPrompt(text) },
            },
            ["temperature"] = 0.1,
            ["max_tokens"] = 512,
        }.ToJsonString();

        using var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage resp = _http.Send(req);
        resp.EnsureSuccessStatusCode();
        using var stream = resp.Content.ReadAsStream();
        using var doc = JsonDocument.Parse(stream);
        return (doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString() ?? "").Trim();
    }
}
