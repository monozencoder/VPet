using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VPet_Simulator.Windows.Interface;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// ペット(セーブデータ)ごとの会話履歴と長期記憶(要約)をディスクに永続化する。
    /// APIキー等の秘匿情報は含まないが、会話内容自体はこのPC上にファイルとして残る点に注意。
    /// </summary>
    public class ChatMemoryStore
    {
        public List<ChatMessage> History { get; set; } = new List<ChatMessage>();
        /// <summary>直近の会話履歴が上限を超えて捨てられる際に、LLMで要約して積み立てていく長期記憶</summary>
        public string Summary { get; set; } = "";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "default";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string FilePathFor(string petName) =>
            Path.Combine(ExtensionValue.GetMODStorage("LLMChat"), $"memory_{SanitizeFileName(petName)}.json");

        public static ChatMemoryStore Load(string petName)
        {
            try
            {
                var path = FilePathFor(petName);
                if (!File.Exists(path))
                    return new ChatMemoryStore();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ChatMemoryStore>(json, JsonOptions) ?? new ChatMemoryStore();
            }
            catch
            {
                return new ChatMemoryStore();
            }
        }

        public void Save(string petName)
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(FilePathFor(petName), json);
            }
            catch
            {
                // 記憶の保存に失敗しても会話自体は継続できるよう、ここでは無視する
            }
        }

        public static void Clear(string petName)
        {
            try
            {
                var path = FilePathFor(petName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
