using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using VPet.Mod.LLMChat.Voice;

namespace VPet.Mod.LLMChat.AiVoiceBridge
{
    /// <summary>
    /// A.I.VOICE Editor公式API(AI.Talk.Editor.Api.dll)は、net8.0のMOD本体プロセスからは
    /// WCF(System.ServiceModel)の厳密名アセンブリ非互換のため直接呼び出せない。
    /// このnet48の別プロセスから代わりに呼び出し、結果をJSONファイルへ書き出す仲介役。
    /// AI.Talk.Editor.Api.dllは実行時までインストールの有無が分からないため、
    /// ビルド時参照は持たずリフレクション(dynamic)経由で読み込む。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("使い方: VPet.Mod.LLMChat.AiVoiceBridge.exe <list-presets|speak> <requestJsonPath> <responseJsonPath>");
                return 1;
            }

            var command = args[0];
            var requestPath = args[1];
            var responsePath = args[2];

            try
            {
                var request = JsonSerializer.Deserialize<AiVoiceRequest>(File.ReadAllText(requestPath))
                    ?? throw new InvalidOperationException("リクエストの解析に失敗しました");

                var response = Execute(command, request);
                File.WriteAllText(responsePath, JsonSerializer.Serialize(response));
                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                TryWriteError(responsePath, ex.Message);
                return 1;
            }
        }

        private static AiVoiceResponse Execute(string command, AiVoiceRequest request)
        {
            var dllPath = ResolveApiDllPath(request.InstallDir);
            var assembly = Assembly.LoadFrom(dllPath);
            var type = assembly.GetType("AI.Talk.Editor.Api.TtsControl")
                ?? throw new InvalidOperationException("AI.Talk.Editor.Api.dllの構成が想定と異なります(TtsControlが見つかりません)");
            dynamic ctrl = Activator.CreateInstance(type);

            string[] hosts = ctrl.GetAvailableHostNames();
            if (hosts == null || hosts.Length == 0)
                throw new InvalidOperationException("A.I.VOICE Editorが見つかりません(インストールされていないか、対応していないバージョンです)");

            ctrl.Initialize(hosts[0]);
            var justStarted = string.Equals(ctrl.Status.ToString(), "NotRunning", StringComparison.Ordinal);
            if (justStarted)
            {
                // 未起動だった場合のみこちらでA.I.VOICE Editorを起動する。ユーザーが手動で
                // 開いていた場合は触らない
                ctrl.StartHost();
            }
            // 起動直後はEditor側のホストサービス初期化がまだ終わっておらずConnect()が失敗することがあるため、
            // 自分で起動した場合は接続できるようになるまでリトライする(既に起動済みなら1回で足りるはず)
            Connect(ctrl, justStarted ? TimeSpan.FromSeconds(60) : TimeSpan.Zero);
            try
            {
                switch (command)
                {
                    case "list-presets":
                        {
                            string[] presets = ctrl.VoicePresetNames;
                            return new AiVoiceResponse { Success = true, Presets = presets ?? Array.Empty<string>() };
                        }
                    case "speak":
                        {
                            if (string.IsNullOrWhiteSpace(request.Text))
                                throw new ArgumentException("読み上げるテキストがありません");
                            if (string.IsNullOrWhiteSpace(request.OutputWavPath))
                                throw new ArgumentException("出力先が指定されていません");

                            if (!string.IsNullOrWhiteSpace(request.PresetName))
                                ctrl.CurrentVoicePresetName = request.PresetName;
                            ctrl.Text = request.Text;
                            ctrl.SaveAudioToFile(request.OutputWavPath);
                            return new AiVoiceResponse { Success = true };
                        }
                    default:
                        throw new ArgumentException($"不明なコマンドです: {command}");
                }
            }
            finally
            {
                try { ctrl.Disconnect(); } catch { /* 後片付け失敗は無視 */ }
            }
        }

        /// <summary>Connect()を試み、retryWithin指定時間内なら失敗しても1秒おきにリトライする</summary>
        private static void Connect(dynamic ctrl, TimeSpan retryWithin)
        {
            var deadline = DateTime.UtcNow + retryWithin;
            while (true)
            {
                try
                {
                    ctrl.Connect();
                    return;
                }
                catch (Exception) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(1000);
                }
            }
        }

        private static string ResolveApiDllPath(string installDir)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(installDir))
                candidates.Add(Path.Combine(installDir, "AI.Talk.Editor.Api.dll"));
            candidates.Add(@"C:\Program Files\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll");
            candidates.Add(@"C:\Program Files (x86)\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll");

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            throw new FileNotFoundException("A.I.VOICE EditorのAPI(AI.Talk.Editor.Api.dll)が見つかりません。A.I.VOICE Editorがインストールされているか確認してください。");
        }

        private static void TryWriteError(string responsePath, string message)
        {
            try
            {
                File.WriteAllText(responsePath, JsonSerializer.Serialize(new AiVoiceResponse { Success = false, Error = message }));
            }
            catch
            {
                // 応答ファイルすら書けない状況では諦める(呼び出し側は終了コードとファイル欠如から異常として扱う)
            }
        }
    }
}
