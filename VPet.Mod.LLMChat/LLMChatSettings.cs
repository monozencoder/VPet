using System;
using System.IO;
using System.Text.Json;
using VPet_Simulator.Windows.Interface;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// 対応するLLMプロバイダーの種類
    /// </summary>
    public enum LlmProviderKind
    {
        Claude,
        OpenAI,
        DeepSeek,
        Custom,
    }

    /// <summary>
    /// LLMChat MODの非秘匿設定(APIキーは含まない)。
    /// APIキーは <see cref="CredentialStore"/> 経由でWindows資格情報マネージャーに保存する。
    /// </summary>
    public class LLMChatSettings
    {
        public LlmProviderKind Provider { get; set; } = LlmProviderKind.Claude;
        public string Model { get; set; } = "claude-opus-5";
        /// <summary>Provider == Customのときに使用するエンドポイントURL(OpenAI Chat Completions互換)</summary>
        public string CustomEndpoint { get; set; } = "";
        /// <summary>キャラクター設定等のシステムプロンプト</summary>
        public string SystemPrompt { get; set; } = "";

        /// <summary>ときどきキャラクターから自発的に話しかけるか</summary>
        public bool ProactiveChatEnabled { get; set; } = false;
        /// <summary>自発的な会話をチェックする間隔(分)</summary>
        public int ProactiveChatIntervalMinutes { get; set; } = 20;
        /// <summary>間隔が経過した時に実際に話しかける確率(%)</summary>
        public int ProactiveChatChancePercent { get; set; } = 40;

        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        private static string SettingsFilePath => Path.Combine(ExtensionValue.GetMODStorage("LLMChat"), FileName);

        public static LLMChatSettings Load()
        {
            try
            {
                var path = SettingsFilePath;
                if (!File.Exists(path))
                    return new LLMChatSettings();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<LLMChatSettings>(json, JsonOptions) ?? new LLMChatSettings();
            }
            catch
            {
                return new LLMChatSettings();
            }
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }

        /// <summary>選択中プロバイダーに対応するCredentialStoreのキー</summary>
        public string CredentialKey => Provider.ToString();

        /// <summary>選択中プロバイダーのデフォルトモデル名</summary>
        public static string DefaultModelFor(LlmProviderKind provider) => provider switch
        {
            LlmProviderKind.Claude => "claude-opus-5",
            LlmProviderKind.OpenAI => "gpt-4o-mini",
            LlmProviderKind.DeepSeek => "deepseek-v4-flash",
            _ => "",
        };
    }
}
