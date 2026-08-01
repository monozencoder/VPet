using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// VOICEVOX(ローカルで起動するVOICEVOX ENGINE)の話者スタイル1件分
    /// </summary>
    public class VoicevoxSpeakerStyle
    {
        public string SpeakerName { get; set; }
        public string StyleName { get; set; }
        public int Id { get; set; }

        public override string ToString() => $"{SpeakerName} ({StyleName})";
    }

    /// <summary>
    /// VOICEVOX ENGINE(http://127.0.0.1:50021 等)のHTTP APIクライアント
    /// </summary>
    public class VoicevoxClient
    {
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string endpoint;

        public VoicevoxClient(string endpoint)
        {
            this.endpoint = string.IsNullOrWhiteSpace(endpoint)
                ? "http://127.0.0.1:50021"
                : endpoint.TrimEnd('/');
        }

        public async Task<List<VoicevoxSpeakerStyle>> GetSpeakersAsync(CancellationToken cancellationToken)
        {
            using var response = await SharedClient.GetAsync($"{endpoint}/speakers", cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"VOICEVOX /speakersエラー({(int)response.StatusCode}): {text}");

            var speakers = JsonSerializer.Deserialize<List<SpeakerEntry>>(text) ?? new List<SpeakerEntry>();
            var result = new List<VoicevoxSpeakerStyle>();
            foreach (var speaker in speakers)
            {
                if (speaker.Styles == null)
                    continue;
                foreach (var style in speaker.Styles)
                {
                    result.Add(new VoicevoxSpeakerStyle
                    {
                        SpeakerName = speaker.Name,
                        StyleName = style.Name,
                        Id = style.Id,
                    });
                }
            }
            return result;
        }

        public async Task<byte[]> SynthesizeAsync(string text, int speakerId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("読み上げるテキストがありません", nameof(text));

            var queryUrl = $"{endpoint}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}";
            using var queryResponse = await SharedClient.PostAsync(queryUrl, content: null, cancellationToken).ConfigureAwait(false);
            var queryText = await queryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!queryResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"VOICEVOX audio_queryエラー({(int)queryResponse.StatusCode}): {queryText}");

            var synthesisUrl = $"{endpoint}/synthesis?speaker={speakerId}";
            using var request = new HttpRequestMessage(HttpMethod.Post, synthesisUrl)
            {
                Content = new StringContent(queryText, Encoding.UTF8, "application/json"),
            };
            using var synthesisResponse = await SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!synthesisResponse.IsSuccessStatusCode)
            {
                var errText = await synthesisResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"VOICEVOX synthesisエラー({(int)synthesisResponse.StatusCode}): {errText}");
            }
            return await synthesisResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        private class SpeakerEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("styles")]
            public List<StyleEntry> Styles { get; set; }
        }

        private class StyleEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("id")]
            public int Id { get; set; }
        }
    }
}
