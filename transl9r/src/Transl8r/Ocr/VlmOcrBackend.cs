using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Transl8r.Config;
using Transl8r.Imaging;
using Transl8r.Net;

namespace Transl8r.Ocr;

/// <summary>
/// Plain VLM OCR against an OpenAI-compatible /v1/chat/completions endpoint:
/// transcribe JA text only (translation handled separately by an ITranslator).
/// Base class for <see cref="VlmDirectBackend"/>; holds the shared HTTP machinery
/// incl. the reasoning_effort/json-mode learn-once 400 fallback (HANDOVER #5).
/// </summary>
internal class VlmOcrBackend : IOcrBackend
{
    private const string TranscribePrompt =
        "Transcribe the Japanese text visible in this image, exactly as written, " +
        "preserving the original Japanese. Output ONLY the transcribed text. If " +
        "there is no legible Japanese text, output an empty response.";

    private readonly string _url;
    private readonly string _model;
    private readonly HttpClient _http;

    // flipped false on the first server rejection, then we retry plain
    private bool _extrasOk = true;
    private bool _jsonModeOk = true;

    public VlmOcrBackend(AppConfig cfg)
    {
        _url = cfg.VlmUrl.TrimEnd('/') + "/v1/chat/completions";
        _model = cfg.VlmModel;
        _http = IPv4HttpClient.Create(TimeSpan.FromSeconds(35));
    }

    public string Recognize(Bitmap image)
    {
        string text = Ask(ImageOps.ToPngBase64(image), TranscribePrompt, 300, forceJson: false);
        // VLMs sometimes narrate instead of staying silent — no JA = no text
        if (text.Length > 0 && !JapaneseText.LooksJapanese(text))
        {
            Debug.WriteLine($"[vlm] dropped non-Japanese reply: {Trunc(text, 200)}");
            return "";
        }
        return text;
    }

    protected string Ask(string b64, string prompt, int maxTokens, bool forceJson)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string payload = BuildPayload(b64, prompt, maxTokens, forceJson);
            using var req = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage resp = _http.Send(req);

            if (resp.StatusCode == HttpStatusCode.BadRequest && (_extrasOk || _jsonModeOk))
            {
                _extrasOk = false;
                _jsonModeOk = false;
                continue; // retry without the optional fields
            }

            resp.EnsureSuccessStatusCode();
            using var stream = resp.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);
            string content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content").GetString() ?? "";
            return JapaneseText.StripThink(content);
        }
        return "";
    }

    private string BuildPayload(string b64, string prompt, int maxTokens, bool forceJson)
    {
        var obj = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = $"data:image/png;base64,{b64}",
                            },
                        },
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = prompt,
                        },
                    },
                },
            },
            ["temperature"] = 0.0,
            ["max_tokens"] = maxTokens,
        };
        if (_extrasOk)
        {
            obj["reasoning_effort"] = "none";
        }
        if (forceJson && _jsonModeOk)
        {
            obj["response_format"] = new JsonObject { ["type"] = "json_object" };
        }
        return obj.ToJsonString();
    }

    protected static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
