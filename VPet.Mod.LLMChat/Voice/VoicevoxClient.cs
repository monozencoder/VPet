using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// VOICEVOX/AivisSpeech ENGINEの「プリセット」1件分。話者スタイルに加え、話速・音高等が
    /// エンジン側UIで事前に設定されており、そのIDを指定するだけでそれらをまとめて反映できる
    /// </summary>
    public class VoicevoxPreset
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int StyleId { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 合成時に話速・音高等をエンジン側の既定値/プリセットから上書きするための調整値。
    /// audio_query/audio_query_from_presetが返すJSONの対応フィールドをこの値で書き換えてから/synthesisへ渡す
    /// </summary>
    public class VoicevoxSynthesisAdjustments
    {
        public double SpeedScale { get; set; } = 1.0;
        public double PitchScale { get; set; } = 0.0;
        public double IntonationScale { get; set; } = 1.0;
        /// <summary>AivisSpeech固有の拡張フィールド。VOICEVOX ENGINEのクエリには存在しないため、その場合は無視される</summary>
        public double TempoDynamicsScale { get; set; } = 1.0;
        public double VolumeScale { get; set; } = 1.0;
        public double PrePhonemeLength { get; set; } = 0.1;
        public double PostPhonemeLength { get; set; } = 0.1;
    }

    /// <summary>
    /// VOICEVOX ENGINE(http://127.0.0.1:50021 等)のHTTP APIクライアント。
    /// AivisSpeech EngineはVOICEVOX ENGINE互換のHTTP API(/speakers, /audio_query, /synthesis)を
    /// 実装しているフォークのため、エンドポイントを変えるだけでこのクライアントをそのまま利用できる
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

        public async Task<byte[]> SynthesizeAsync(string text, int speakerId, VoicevoxSynthesisAdjustments adjustments, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("読み上げるテキストがありません", nameof(text));

            var queryUrl = $"{endpoint}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}";
            var queryText = await PostForQueryTextAsync(queryUrl, cancellationToken).ConfigureAwait(false);
            queryText = ApplyAdjustments(queryText, adjustments);
            return await SynthesizeFromQueryAsync(queryText, speakerId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<VoicevoxPreset>> GetPresetsAsync(CancellationToken cancellationToken)
        {
            using var response = await SharedClient.GetAsync($"{endpoint}/presets", cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"/presetsエラー({(int)response.StatusCode}): {text}");

            var presets = JsonSerializer.Deserialize<List<PresetEntry>>(text) ?? new List<PresetEntry>();
            var result = new List<VoicevoxPreset>();
            foreach (var preset in presets)
            {
                result.Add(new VoicevoxPreset
                {
                    Id = preset.Id,
                    Name = preset.Name,
                    StyleId = preset.StyleId,
                });
            }
            return result;
        }

        /// <summary>プリセットIDを指定して合成する。プリセットに設定された話速・音高・抑揚等がまとめて反映される</summary>
        public async Task<byte[]> SynthesizeWithPresetAsync(string text, int presetId, int styleId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("読み上げるテキストがありません", nameof(text));

            var queryUrl = $"{endpoint}/audio_query_from_preset?text={Uri.EscapeDataString(text)}&preset_id={presetId}";
            var queryText = await PostForQueryTextAsync(queryUrl, cancellationToken).ConfigureAwait(false);
            return await SynthesizeFromQueryAsync(queryText, styleId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// audio_query系エンドポイントが返したクエリJSONに、話速等の調整値を上書きする。
        /// tempoDynamicsScaleはAivisSpeech固有の拡張フィールドのため、キーが存在する場合のみ上書きする(VOICEVOXでは無視)
        /// </summary>
        private static string ApplyAdjustments(string queryJson, VoicevoxSynthesisAdjustments adjustments)
        {
            if (adjustments == null)
                return queryJson;

            var node = JsonNode.Parse(queryJson)?.AsObject()
                ?? throw new InvalidOperationException("audio_queryの応答を解析できませんでした");
            node["speedScale"] = adjustments.SpeedScale;
            node["pitchScale"] = adjustments.PitchScale;
            node["intonationScale"] = adjustments.IntonationScale;
            node["volumeScale"] = adjustments.VolumeScale;
            node["prePhonemeLength"] = adjustments.PrePhonemeLength;
            node["postPhonemeLength"] = adjustments.PostPhonemeLength;
            if (node.ContainsKey("tempoDynamicsScale"))
                node["tempoDynamicsScale"] = adjustments.TempoDynamicsScale;
            return node.ToJsonString();
        }

        private static async Task<string> PostForQueryTextAsync(string queryUrl, CancellationToken cancellationToken)
        {
            using var queryResponse = await SharedClient.PostAsync(queryUrl, content: null, cancellationToken).ConfigureAwait(false);
            var queryText = await queryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!queryResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"VOICEVOX audio_queryエラー({(int)queryResponse.StatusCode}): {queryText}");
            return queryText;
        }

        private async Task<byte[]> SynthesizeFromQueryAsync(string queryText, int speakerId, CancellationToken cancellationToken)
        {
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

        private class PresetEntry
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("style_id")]
            public int StyleId { get; set; }
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
