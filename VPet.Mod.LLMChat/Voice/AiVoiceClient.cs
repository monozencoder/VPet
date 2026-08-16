using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// A.I.VOICE Editorとの連携クライアント。
    /// 公式API(AI.Talk.Editor.Api.dll)はWCF(System.ServiceModel)前提で、net8.0の本体プロセスからは
    /// 厳密名アセンブリの非互換により直接呼び出せない。そのためSystem.ServiceModelが標準搭載の
    /// net48でビルドした別プロセス(VPet.Mod.LLMChat.AiVoiceBridge.exe)を都度起動し、
    /// リクエスト/レスポンスをJSONファイル経由でやり取りする。
    /// </summary>
    public class AiVoiceClient
    {
        // A.I.VOICE Editor未起動時は、起動待ちに加えてブリッジ側で接続できるまで最大60秒リトライするため、
        // それより余裕を持たせる
        private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(90);

        private readonly string installDir;

        public AiVoiceClient(string installDir)
        {
            this.installDir = installDir;
        }

        /// <summary>A.I.VOICE Editorに登録されているボイスプリセット名の一覧を取得する</summary>
        public Task<AiVoiceResponse> GetPresetsAsync(CancellationToken cancellationToken)
        {
            var request = new AiVoiceRequest { InstallDir = installDir };
            return RunBridgeAsync("list-presets", request, cancellationToken);
        }

        public async Task<byte[]> SynthesizeAsync(string text, string presetName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("読み上げるテキストがありません", nameof(text));

            var wavPath = Path.Combine(Path.GetTempPath(), $"vpet_aivoice_{Guid.NewGuid():N}.wav");
            try
            {
                var request = new AiVoiceRequest
                {
                    InstallDir = installDir,
                    PresetName = presetName,
                    Text = text,
                    OutputWavPath = wavPath,
                };
                await RunBridgeAsync("speak", request, cancellationToken).ConfigureAwait(false);
                return await File.ReadAllBytesAsync(wavPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(wavPath);
            }
        }

        private static async Task<AiVoiceResponse> RunBridgeAsync(string command, AiVoiceRequest request, CancellationToken cancellationToken)
        {
            var bridgePath = ResolveBridgePath();
            var requestPath = Path.Combine(Path.GetTempPath(), $"vpet_aivoice_req_{Guid.NewGuid():N}.json");
            var responsePath = Path.Combine(Path.GetTempPath(), $"vpet_aivoice_res_{Guid.NewGuid():N}.json");
            try
            {
                await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);

                var psi = new ProcessStartInfo
                {
                    FileName = bridgePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(command);
                psi.ArgumentList.Add(requestPath);
                psi.ArgumentList.Add(responsePath);

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("A.I.VOICE連携プロセスを起動できませんでした");

                // A.I.VOICE Editor未起動時は初回のみ起動待ちで数秒〜十数秒かかるため、通常のHTTP系より長めに待つ
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(BridgeTimeout);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    throw new TimeoutException("A.I.VOICE Editorとの通信がタイムアウトしました(起動に時間がかかっている可能性があります)");
                }

                if (!File.Exists(responsePath))
                    throw new InvalidOperationException($"A.I.VOICE連携プロセスが応答を返しませんでした(終了コード: {process.ExitCode})");

                var responseJson = await File.ReadAllTextAsync(responsePath, cancellationToken).ConfigureAwait(false);
                var response = JsonSerializer.Deserialize<AiVoiceResponse>(responseJson)
                    ?? throw new InvalidOperationException("A.I.VOICE連携プロセスの応答を解析できませんでした");
                if (!response.Success)
                    throw new InvalidOperationException(response.Error ?? "不明なエラー");
                return response;
            }
            finally
            {
                TryDelete(requestPath);
                TryDelete(responsePath);
            }
        }

        private static string ResolveBridgePath()
        {
            // VPet本体のMODローダーはpluginフォルダ直下の*.dllを全てAssembly.LoadFromするため、
            // ブリッジexeの依存DLL(System.Text.Json等)が本体の同名アセンブリと衝突しないよう
            // サブフォルダ(aivoice-bridge)に隔離して配置している(VPet.Mod.LLMChat.AiVoiceBridge.csproj参照)
            var dir = Path.GetDirectoryName(typeof(AiVoiceClient).Assembly.Location);
            var path = Path.Combine(dir ?? string.Empty, "aivoice-bridge", "VPet.Mod.LLMChat.AiVoiceBridge.exe");
            if (!File.Exists(path))
                throw new FileNotFoundException("A.I.VOICE連携プログラム(VPet.Mod.LLMChat.AiVoiceBridge.exe)が見つかりません", path);
            return path;
        }

        private static void TryKill(Process process)
        {
            try { process.Kill(true); } catch { /* 既に終了している等は無視 */ }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 後片付け失敗は無視 */ }
        }
    }
}
