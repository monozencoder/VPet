using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Providers
{
    /// <summary>
    /// Anthropic Claude (Messages API) 用プロバイダー。ストリーミング(SSE)で応答を受け取る。
    /// </summary>
    public class ClaudeChatProvider : ILlmChatProvider
    {
        private const string DefaultBaseUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        private readonly string apiKey;
        private readonly string model;
        private readonly int maxTokens;

        public ClaudeChatProvider(string apiKey, string model, int maxTokens = 1024)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model;
            this.maxTokens = maxTokens;
        }

        public async Task<string> ChatStreamAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, Action<string> onDelta, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Claude APIキーが設定されていません");

            var requestBody = new ClaudeRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Stream = true,
                System = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
                Messages = history.Select(m => new ClaudeMessage { Role = m.Role, Content = m.Content }).ToList(),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, DefaultBaseUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions.Default);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string message;
                try
                {
                    var err = JsonSerializer.Deserialize<ClaudeErrorResponse>(responseText, JsonOptions.Default);
                    message = err?.Error?.Message ?? responseText;
                }
                catch
                {
                    message = responseText;
                }
                throw new InvalidOperationException($"Claude API错误({(int)response.StatusCode}): {message}");
            }

            var fullText = new StringBuilder();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                    continue;

                var payload = line.Substring("data: ".Length);
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                switch (type)
                {
                    case "content_block_delta":
                        if (root.TryGetProperty("delta", out var delta)
                            && delta.TryGetProperty("type", out var deltaType) && deltaType.GetString() == "text_delta"
                            && delta.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                fullText.Append(text);
                                onDelta(text);
                            }
                        }
                        break;
                    case "error":
                        var errMessage = root.TryGetProperty("error", out var errObj) && errObj.TryGetProperty("message", out var errMsgProp)
                            ? errMsgProp.GetString()
                            : "不明なエラー";
                        throw new InvalidOperationException($"Claude APIストリームエラー: {errMessage}");
                    case "message_stop":
                        return fullText.ToString();
                }
            }

            if (fullText.Length == 0)
                throw new InvalidOperationException("Claudeからの応答が空でした");
            return fullText.ToString();
        }

        private class ClaudeRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
            [JsonPropertyName("stream")]
            public bool Stream { get; set; }
            [JsonPropertyName("system")]
            public string System { get; set; }
            [JsonPropertyName("messages")]
            public List<ClaudeMessage> Messages { get; set; }
        }

        private class ClaudeMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }
            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class ClaudeErrorResponse
        {
            [JsonPropertyName("error")]
            public ClaudeErrorDetail Error { get; set; }
        }

        private class ClaudeErrorDetail
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }

    internal static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
