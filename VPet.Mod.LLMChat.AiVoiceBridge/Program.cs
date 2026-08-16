using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
            using var minimizeCts = new CancellationTokenSource();
            Task minimizeTask = Task.CompletedTask;
            if (justStarted)
            {
                // 未起動だった場合のみこちらでA.I.VOICE Editorを起動する。ユーザーが手動で
                // 開いていた場合は触らないが、自動起動の場合はチャットのたびに画面に出てきて
                // 邪魔にならないよう最小化する(SaveAudioToFile等はウィンドウ状態と無関係に動作する)。
                // 起動シーケンス中はスプラッシュ→本ウィンドウと入れ替わるため、接続完了まで繰り返し最小化し続ける
                ctrl.StartHost();
                minimizeTask = Task.Run(() => KeepMinimizingEditorWindow(minimizeCts.Token));
            }
            try
            {
                // 起動直後はEditor側のホストサービス初期化がまだ終わっておらずConnect()が失敗することがあるため、
                // 自分で起動した場合は接続できるようになるまでリトライする(既に起動済みなら1回で足りるはず)
                Connect(ctrl, justStarted ? TimeSpan.FromSeconds(60) : TimeSpan.Zero);
            }
            finally
            {
                minimizeCts.Cancel();
                minimizeTask.Wait();
            }
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

        private const int SW_MINIMIZE = 6;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// A.I.VOICE Editorの本来のメインウィンドウを見つけ次第最小化し、接続完了までポーリングを続ける。
        /// 起動シーケンス中はスプラッシュ画面→本来のメインウィンドウと入れ替わるため一度だけでは足りないが、
        /// スプラッシュ画面自体を最小化すると起動シーケンスがそこで止まってしまう(実機で確認済み)ため、
        /// タイトルが"Splash Screen"の間は何もせず、本来のウィンドウが出てきてから最小化する
        /// </summary>
        private static void KeepMinimizingEditorWindow(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var proc in Process.GetProcessesByName("AIVoiceEditor"))
                {
                    if (proc.MainWindowHandle != IntPtr.Zero && proc.MainWindowTitle != "Splash Screen")
                        ShowWindow(proc.MainWindowHandle, SW_MINIMIZE);
                }
                token.WaitHandle.WaitOne(200);
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
