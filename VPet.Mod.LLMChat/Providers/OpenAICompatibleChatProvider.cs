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
    /// OpenAI Chat Completions API 互換のプロバイダー。ストリーミング(SSE)で応答を受け取る。
    /// ChatGPT(OpenAI)・DeepSeek・および将来のOpenAI互換API(Gemini互換エンドポイント等)を
    /// BaseUrl/Model/ApiKeyのみの差し替えでカバーする汎用実装。
    /// </summary>
    public class OpenAICompatibleChatProvider : ILlmChatProvider
    {
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        private readonly string endpoint;
        private readonly string apiKey;
        private readonly string model;
        private readonly int maxTokens;

        public OpenAICompatibleChatProvider(string endpoint, string apiKey, string model, int maxTokens = 1024)
        {
            this.endpoint = endpoint;
            this.apiKey = apiKey;
            this.model = model;
            this.maxTokens = maxTokens;
        }

        public async Task<string> ChatStreamAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, Action<string> onDelta, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("APIのエンドポイントURLが設定されていません");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("APIキーが設定されていません");
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("モデル名が設定されていません");

            var messages = new List<OpenAIMessage>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new OpenAIMessage { Role = "system", Content = systemPrompt });
            messages.AddRange(history.Select(m => new OpenAIMessage { Role = m.Role, Content = m.Content }));

            var requestBody = new OpenAIRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Stream = true,
                Messages = messages,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            var json = JsonSerializer.Serialize(requestBody, JsonOptions.Default);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string message;
                try
                {
                    var err = JsonSerializer.Deserialize<OpenAIErrorResponse>(responseText, JsonOptions.Default);
                    message = err?.Error?.Message ?? responseText;
                }
                catch
                {
                    message = responseText;
                }
                throw new InvalidOperationException($"API错误({(int)response.StatusCode}): {message}");
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
                if (payload == "[DONE]")
                    break;

                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var contentProp)
                    && contentProp.ValueKind == JsonValueKind.String)
                {
                    var text = contentProp.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        fullText.Append(text);
                        onDelta(text);
                    }
                }
            }

            if (fullText.Length == 0)
                throw new InvalidOperationException("応答が空でした");
            return fullText.ToString();
        }

        private class OpenAIRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }
            [JsonPropertyName("messages")]
            public List<OpenAIMessage> Messages { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
            [JsonPropertyName("stream")]
            public bool Stream { get; set; }
        }

        private class OpenAIMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }
            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class OpenAIErrorResponse
        {
            [JsonPropertyName("error")]
            public OpenAIErrorDetail Error { get; set; }
        }

        private class OpenAIErrorDetail
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }
}
