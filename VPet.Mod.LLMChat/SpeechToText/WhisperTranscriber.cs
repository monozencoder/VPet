using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.SpeechToText
{
    /// <summary>
    /// OpenAI Whisper(音声認識)APIクライアント
    /// </summary>
    public class WhisperTranscriber
    {
        private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly string apiKey;
        private readonly string model;

        public WhisperTranscriber(string apiKey, string model)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrWhiteSpace(model) ? "whisper-1" : model;
        }

        public async Task<string> TranscribeAsync(byte[] wavData, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("音声入力用のOpenAI APIキーが設定されていません");
            if (wavData == null || wavData.Length == 0)
                throw new InvalidOperationException("録音データが空です");

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "input.wav");
            content.Add(new StringContent(model), "model");

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Whisper APIエラー({(int)response.StatusCode}): {responseText}");

            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.TryGetProperty("text", out var textProp) ? textProp.GetString() : string.Empty;
        }
    }
}
