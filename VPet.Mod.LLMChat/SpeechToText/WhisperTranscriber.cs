using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.SpeechToText
{
    /// <summary>
    /// Whisper(音声認識)のOpenAI互換Transcription APIクライアント。
    /// OpenAIのクラウドAPIだけでなく、同じ形式(multipart/form-data + /v1/audio/transcriptions)を
    /// 実装するローカルサーバー(faster-whisper-server等)にもエンドポイントを変えるだけで対応できる
    /// </summary>
    public class WhisperTranscriber
    {
        private const string DefaultOpenAiEndpoint = "https://api.openai.com/v1/audio/transcriptions";
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly string endpoint;
        private readonly string apiKey;
        private readonly string model;

        /// <summary>endpointを省略/空欄にした場合はOpenAIのクラウドAPIを使う</summary>
        public WhisperTranscriber(string apiKey, string model, string endpoint = null)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrWhiteSpace(model) ? "whisper-1" : model;
            this.endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultOpenAiEndpoint : endpoint.Trim();
        }

        public async Task<string> TranscribeAsync(byte[] wavData, CancellationToken cancellationToken)
        {
            if (wavData == null || wavData.Length == 0)
                throw new InvalidOperationException("録音データが空です");

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "input.wav");
            content.Add(new StringContent(model), "model");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            // ローカルサーバーの多くは認証不要のため、APIキーが未設定でもヘッダーを付けずに送る
            if (!string.IsNullOrEmpty(apiKey))
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
