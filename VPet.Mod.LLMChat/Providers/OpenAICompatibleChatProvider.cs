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
    /// OpenAI Chat Completions API 互換のプロバイダー。
    /// ChatGPT(OpenAI)・DeepSeek・および将来のOpenAI互換API(Gemini互換エンドポイント等)を
    /// BaseUrl/Model/ApiKeyのみの差し替えでカバーする汎用実装。
    /// </summary>
    public class OpenAICompatibleChatProvider : ILlmChatProvider
    {
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

        public async Task<string> ChatAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
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
                Messages = messages,
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            var json = JsonSerializer.Serialize(requestBody, JsonOptions.Default);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
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

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseText, JsonOptions.Default);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException("応答が空でした");
            return text;
        }

        private class OpenAIRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }
            [JsonPropertyName("messages")]
            public List<OpenAIMessage> Messages { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
        }

        private class OpenAIMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }
            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class OpenAIResponse
        {
            [JsonPropertyName("choices")]
            public List<OpenAIChoice> Choices { get; set; }
        }

        private class OpenAIChoice
        {
            [JsonPropertyName("message")]
            public OpenAIMessage Message { get; set; }
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
