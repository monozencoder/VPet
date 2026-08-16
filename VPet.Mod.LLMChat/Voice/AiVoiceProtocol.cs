namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// VPet.Mod.LLMChat(net8.0)とVPet.Mod.LLMChat.AiVoiceBridge(net48)プロセス間で
    /// JSONファイル経由でやり取りするリクエスト/レスポンスの形式。
    /// このファイルは両プロジェクトからリンク参照される。
    /// </summary>
    public class AiVoiceRequest
    {
        /// <summary>A.I.VOICE Editorのインストールフォルダ(空欄なら既定のインストール場所を自動探索)</summary>
        public string InstallDir { get; set; }
        /// <summary>読み上げに使うボイスプリセット名(speakコマンドで空欄ならEditor側で選択中のものを使用)</summary>
        public string PresetName { get; set; }
        /// <summary>読み上げるテキスト(speakコマンドで使用)</summary>
        public string Text { get; set; }
        /// <summary>合成した音声(wav)の保存先(speakコマンドで使用)</summary>
        public string OutputWavPath { get; set; }
    }

    public class AiVoiceResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        /// <summary>list-presetsコマンドで取得したボイスプリセット名一覧</summary>
        public string[] Presets { get; set; }
    }
}
