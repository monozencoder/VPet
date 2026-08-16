using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// OpenAIのクラウドTTS(音声合成)APIクライアント
    /// </summary>
    public class OpenAiTtsClient
    {
        private const string Endpoint = "https://api.openai.com/v1/audio/speech";
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // OpenAIのresponse_format=pcmは24kHz/16bit/モノラルのヘッダー無しリトルエンディアンPCM固定
        private const int PcmSampleRate = 24000;
        private const short PcmBitsPerSample = 16;
        private const short PcmChannels = 1;

        public async Task<byte[]> SynthesizeAsync(string text, string apiKey, string model, string voice, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("読み上げるテキストがありません", nameof(text));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("OpenAI TTS用のAPIキーが設定されていません");

            // response_format=wavはストリーミング由来でRIFFヘッダーのデータ長が不正なことがあり、
            // NAudioでの再生時に例外(Stream length must be non-negative...)になるため、
            // ヘッダー無しの生PCMを取得しこちらで正しいWAVヘッダーを付与する
            var body = JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini-tts" : model,
                voice = string.IsNullOrWhiteSpace(voice) ? "alloy" : voice,
                input = text,
                response_format = "pcm",
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"OpenAI TTS APIエラー({(int)response.StatusCode}): {errText}");
            }
            var pcm = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return WrapPcmAsWav(pcm);
        }

        private static byte[] WrapPcmAsWav(byte[] pcm)
        {
            var byteRate = PcmSampleRate * PcmChannels * (PcmBitsPerSample / 8);
            var blockAlign = (short)(PcmChannels * (PcmBitsPerSample / 8));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write(PcmChannels);
                writer.Write(PcmSampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(PcmBitsPerSample);

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);
                writer.Write(pcm);
            }
            return stream.ToArray();
        }
    }
}
