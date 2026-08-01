using System;
using System.Collections.Generic;
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
    /// Anthropic Claude (Messages API) 用プロバイダー
    /// </summary>
    public class ClaudeChatProvider : ILlmChatProvider
    {
        private const string DefaultBaseUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private readonly string apiKey;
        private readonly string model;
        private readonly int maxTokens;

        public ClaudeChatProvider(string apiKey, string model, int maxTokens = 1024)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model;
            this.maxTokens = maxTokens;
        }

        public async Task<string> ChatAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Claude APIキーが設定されていません");

            var requestBody = new ClaudeRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                System = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
                Messages = history.Select(m => new ClaudeMessage { Role = m.Role, Content = m.Content }).ToList(),
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var request = new HttpRequestMessage(HttpMethod.Post, DefaultBaseUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions.Default);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
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

            var result = JsonSerializer.Deserialize<ClaudeResponse>(responseText, JsonOptions.Default);
            if (result?.StopReason == "refusal")
                throw new InvalidOperationException("Claudeが安全上の理由で応答を拒否しました");

            var text = result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException("Claudeからの応答が空でした");
            return text;
        }

        private class ClaudeRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
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

        private class ClaudeResponse
        {
            [JsonPropertyName("content")]
            public List<ClaudeContentBlock> Content { get; set; }
            [JsonPropertyName("stop_reason")]
            public string StopReason { get; set; }
        }

        private class ClaudeContentBlock
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }
            [JsonPropertyName("text")]
            public string Text { get; set; }
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
