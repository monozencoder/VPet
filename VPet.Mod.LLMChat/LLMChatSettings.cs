using System;
using System.IO;
using System.Text.Encodings.Web;
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
        //既存の設定ファイルはenumを数値のまま保存しているため、Provider列挙値は必ず末尾に追加すること
        Kimi,
    }

    /// <summary>
    /// 対応する音声入力(STT/Whisper)プロバイダーの種類
    /// </summary>
    public enum SttProviderKind
    {
        /// <summary>OpenAIのクラウドWhisper API</summary>
        OpenAi,
        /// <summary>ローカルで起動する、OpenAI互換のTranscription APIを持つサーバー(faster-whisper-server等)</summary>
        LocalServer,
    }

    /// <summary>
    /// 対応する読み上げ(TTS)プロバイダーの種類
    /// </summary>
    public enum TtsProviderKind
    {
        /// <summary>ローカルで起動するVOICEVOX ENGINE</summary>
        Voicevox,
        /// <summary>OpenAIのクラウドTTS API</summary>
        OpenAi,
        /// <summary>ローカルにインストールされたA.I.VOICE Editor</summary>
        AiVoice,
        //既存の設定ファイルはenumを数値のまま保存しているため、TtsProviderKind列挙値は必ず末尾に追加すること
        /// <summary>ローカルで起動するAivisSpeech Engine(VOICEVOX ENGINE互換API)</summary>
        AivisSpeech,
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
        /// <summary>自発的な会話をチェックする間隔(秒)</summary>
        public int ProactiveChatIntervalSeconds { get; set; } = 1200;
        /// <summary>間隔が経過した時に実際に話しかける確率(%)</summary>
        public int ProactiveChatChancePercent { get; set; } = 40;

        /// <summary>返信を読み上げるか</summary>
        public bool VoiceEnabled { get; set; } = false;
        /// <summary>読み上げに使うTTSプロバイダー</summary>
        public TtsProviderKind TtsProvider { get; set; } = TtsProviderKind.Voicevox;
        /// <summary>読み上げ音量(%)</summary>
        public int VoiceVolumePercent { get; set; } = 100;
        /// <summary>VOICEVOX ENGINEのエンドポイント(事前に起動しておく必要あり)</summary>
        public string VoiceEndpoint { get; set; } = "http://127.0.0.1:50021";
        /// <summary>読み上げに使うVOICEVOXの話者スタイルID(設定画面の「話者一覧を取得」で選択)</summary>
        public int VoiceSpeakerId { get; set; } = 3;
        /// <summary>AivisSpeech Engineのエンドポイント(事前に起動しておく必要あり)</summary>
        public string AivisSpeechEndpoint { get; set; } = "http://127.0.0.1:10101";
        /// <summary>読み上げに使うAivisSpeechの話者スタイルID(設定画面の「話者一覧を取得」で選択)</summary>
        public int AivisSpeechSpeakerId { get; set; } = 0;
        /// <summary>
        /// 読み上げに使うAivisSpeechのプリセットID(設定画面の「プリセット一覧を取得」で選択)。
        /// nullの場合はプリセットを使わず、AivisSpeechSpeakerIdの話者スタイルをそのまま使用する
        /// </summary>
        public int? AivisSpeechPresetId { get; set; } = null;
        /// <summary>AivisSpeechPresetIdが指すプリセットに紐づく話者スタイルID(/synthesis呼び出し用に保持)</summary>
        public int AivisSpeechPresetStyleId { get; set; } = 0;

        /// <summary>
        /// VOICEVOX/AivisSpeech共通の話速・音高等の詳細設定。AivisSpeechでプリセットを選択している場合は
        /// プリセット側の値が優先され、この設定は無視される
        /// </summary>
        public double VoiceSpeedScale { get; set; } = 1.0;
        public double VoicePitchScale { get; set; } = 0.0;
        /// <summary>抑揚・感情表現の強さ(intonationScale)</summary>
        public double VoiceIntonationScale { get; set; } = 1.0;
        /// <summary>テンポの緩急(tempoDynamicsScale)。AivisSpeech固有でVOICEVOXでは無視される</summary>
        public double VoiceTempoDynamicsScale { get; set; } = 1.0;
        /// <summary>合成時の音量倍率(volumeScale)。上の「音量」(再生音量)とは別のパラメータ</summary>
        public double VoiceVolumeScale { get; set; } = 1.0;
        /// <summary>開始無音(秒)</summary>
        public double VoicePrePhonemeLength { get; set; } = 0.1;
        /// <summary>終了無音(秒)</summary>
        public double VoicePostPhonemeLength { get; set; } = 0.1;
        /// <summary>OpenAI TTSで使うモデル名</summary>
        public string OpenAiTtsModel { get; set; } = "gpt-4o-mini-tts";
        /// <summary>OpenAI TTSで使う音声名(alloy, echo, fable, onyx, nova, shimmer等)</summary>
        public string OpenAiTtsVoice { get; set; } = "alloy";
        /// <summary>OpenAI TTS用APIキーのCredentialStoreキー(他の音声機能とは独立)</summary>
        public static string OpenAiTtsCredentialKey => "OpenAI_TTS";
        /// <summary>A.I.VOICE Editorのインストールフォルダ(空欄なら既定のインストール場所を自動探索)</summary>
        public string AiVoiceInstallDir { get; set; } = "";
        /// <summary>読み上げに使うA.I.VOICEのボイスプリセット名(空欄ならA.I.VOICE Editor側で選択中のものを使用)</summary>
        public string AiVoicePresetName { get; set; } = "";

        /// <summary>マイクボタンで音声入力(Whisper)を使うか</summary>
        public bool VoiceInputEnabled { get; set; } = false;
        /// <summary>音声入力に使うプロバイダー</summary>
        public SttProviderKind SttProvider { get; set; } = SttProviderKind.OpenAi;
        /// <summary>SttProvider == LocalServerのときに使う、OpenAI互換のTranscription APIエンドポイント</summary>
        public string VoiceInputEndpoint { get; set; } = "http://127.0.0.1:8000/v1/audio/transcriptions";
        /// <summary>音声入力に使うWhisperのモデル名</summary>
        public string VoiceInputModel { get; set; } = "whisper-1";
        /// <summary>音声認識結果を確認なしで自動的に送信するか</summary>
        public bool VoiceInputAutoSend { get; set; } = true;
        /// <summary>音声入力用APIキーのCredentialStoreキー(チャットのプロバイダー設定とは独立)</summary>
        public static string VoiceInputCredentialKey => "OpenAI_Whisper";
        /// <summary>マイクの録音開始/停止時に短い効果音を鳴らすか</summary>
        public bool MicSoundEnabled { get; set; } = true;

        /// <summary>
        /// ハンズフリー会話モード。有効時はマイクボタンでの手動録音の代わりに、マイクを常時開いたまま
        /// 発話区間を自動検出して送信する(順番待ち方式: キャラクターが話している間はマイクを一時停止する)
        /// </summary>
        public bool HandsFreeModeEnabled { get; set; } = false;
        /// <summary>発話とみなす音量の閾値(0.0～1.0の正規化振幅)。環境ノイズが大きい場合は上げる</summary>
        public double HandsFreeVolumeThreshold { get; set; } = 0.02;
        /// <summary>発話が終わったとみなすまでの無音継続時間(ミリ秒)</summary>
        public int HandsFreeSilenceMs { get; set; } = 900;
        /// <summary>誤検知(物音等)を無視するための最小発話時間(ミリ秒)</summary>
        public int HandsFreeMinSpeechMs { get; set; } = 300;

        /// <summary>会話履歴と長期記憶をディスクに保存し、アプリ再起動後も維持するか</summary>
        public bool MemoryPersistenceEnabled { get; set; } = true;
        /// <summary>長期記憶(要約)の最大文字数。超えた場合は古い部分から失われる</summary>
        public int MemorySummaryMaxChars { get; set; } = 2000;

        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 既定のEncoderはシステムプロンプト等の日本語を\uXXXXにエスケープしてしまい、テキストエディタで
            // 直接確認・編集しづらいため、UTF-8のまま出力する
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
            LlmProviderKind.Kimi => "kimi-k2-0905-preview",
            _ => "",
        };

        /// <summary>
        /// 選択中プロバイダーでよく使われるモデル名の候補一覧(設定画面のドロップダウン用)。
        /// APIの仕様変更で古くなる可能性があるため、一覧にない名前も自由に入力できる
        /// </summary>
        public static string[] KnownModelsFor(LlmProviderKind provider) => provider switch
        {
            LlmProviderKind.Claude => new[] { "claude-opus-5", "claude-sonnet-5", "claude-fable-5", "claude-haiku-4-5-20251001" },
            LlmProviderKind.OpenAI => new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1", "gpt-4.1-mini", "o4-mini" },
            LlmProviderKind.DeepSeek => new[] { "deepseek-v4-flash", "deepseek-chat", "deepseek-reasoner" },
            LlmProviderKind.Kimi => new[] { "kimi-k2-0905-preview", "kimi-k2-turbo-preview", "kimi-latest", "moonshot-v1-8k" },
            _ => Array.Empty<string>(),
        };
    }
}
