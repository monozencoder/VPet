using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VPet.Mod.LLMChat.Providers;
using VPet.Mod.LLMChat.SpeechToText;
using VPet.Mod.LLMChat.Voice;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// LLM(Claude/ChatGPT/DeepSeek/カスタム)を使ったチャットUI
    /// </summary>
    public class LLMTalkBox : TalkBox
    {
        private readonly LLMChatPlugin plugin;
        private readonly List<ChatMessage> history = new List<ChatMessage>();
        private readonly SpeechPlayer voicePlayer;
        private AudioRecorder recorder;
        private bool isRecording;
        private CancellationTokenSource activeRequestCts;
        private const int MaxHistoryMessages = 40;

        /// <summary>直近の会話履歴が上限を超えて捨てられた分を要約して積み立てる長期記憶</summary>
        private string longTermSummary = "";

        private string PetName => plugin.MW?.Core?.Save?.Name;

        public LLMTalkBox(LLMChatPlugin plugin) : base(plugin)
        {
            this.plugin = plugin;
            voicePlayer = new SpeechPlayer(plugin.Settings);
            UpdateMicButtonVisibility();
            // 右クリック等でツールバー(この入力欄を含む)が表示されたら、自動で入力欄にフォーカスする
            IsVisibleChanged += LLMTalkBox_IsVisibleChanged;

            if (plugin.Settings.MemoryPersistenceEnabled)
            {
                var stored = ChatMemoryStore.Load(PetName);
                history.AddRange(stored.History);
                longTermSummary = stored.Summary ?? "";
                TrimHistory();
            }
        }

        /// <summary>設定の音声入力有効フラグに応じてマイクボタンの表示を更新する</summary>
        public void UpdateMicButtonVisibility()
        {
            btnMic.Visibility = plugin.Settings.VoiceInputEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LLMTalkBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
                return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Focusable = true;
                MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
        }

        public override string APIName => "LLM Chat (Claude/ChatGPT/DeepSeek)";

        public override async void Responded(string text)
        {
            InterruptCurrentReply();
            var cts = new CancellationTokenSource();
            activeRequestCts = cts;

            DisplayThink();
            var userMessage = new ChatMessage("user", text);
            history.Add(userMessage);
            TrimHistory();

            // 生成されながら表示するため、ストリーミング用のSayInfoに逐次テキストを流し込む
            var sayInfo = new SayInfoWithStream();
            DisplayThinkToSayRnd(sayInfo);

            try
            {
                var provider = BuildProvider();
                var reply = await provider.ChatStreamAsync(BuildSystemPromptWithMemory(), history, delta => sayInfo.UpdateText(delta), cts.Token).ConfigureAwait(true);
                sayInfo.FinishGenerate();
                history.Add(new ChatMessage("assistant", reply));
                PersistMemory();
                voicePlayer.Speak(reply);
            }
            catch (OperationCanceledException)
            {
                // 新しいメッセージに割り込まれたので、返事を待たずに終わったこの発言は履歴から取り除く
                sayInfo.FinishGenerate();
                history.Remove(userMessage);
            }
            catch (Exception ex)
            {
                // 失敗した発言は履歴から取り除き、再送信できるようにする
                sayInfo.FinishGenerate();
                history.Remove(userMessage);
                DisplayThinkToSayRnd($"エラーが発生しました: {ex.Message}");
            }
            finally
            {
                if (activeRequestCts == cts)
                    activeRequestCts = null;
                cts.Dispose();
            }
        }

        /// <summary>
        /// 相手(キャラクター)が今話している内容を中断する。新しくメッセージを送る/録音を始める際に呼び出す。
        /// Responded()はバックグラウンドスレッドから実行されるため、UI要素の操作はDispatcher経由で行う。
        /// また、MsgBar.ForceClose()は内部タイマーを破棄してしまい以後の表示が壊れるため使わず、
        /// Visibilityを直接畳んで即座に隠すだけに留める(内部状態は次のShow()呼び出しで正しくリセットされる)。
        /// </summary>
        private void InterruptCurrentReply()
        {
            activeRequestCts?.Cancel();
            voicePlayer.Stop();
            Dispatcher.Invoke(() => MainPlugin.MW.Main.MsgBar.Visibility = Visibility.Collapsed);
        }

        /// <summary>
        /// ユーザーの発言なしに、キャラクターから自発的に一言話しかける。
        /// APIキー未設定時や通信エラー時は静かに何もしない(ユーザー操作起点ではないため)。
        /// </summary>
        public async void TriggerProactiveMessage()
        {
            if (!CredentialStore.Exists(plugin.Settings.CredentialKey))
                return;

            activeRequestCts?.Cancel();
            var cts = new CancellationTokenSource();
            activeRequestCts = cts;

            DisplayThink();
            var userMessage = new ChatMessage("user", "（少し時間が経ちました。あなたから飼い主に一言、自然に話しかけてください。挨拶や近況、思ったことなど、短く自然な一言で構いません。）");
            history.Add(userMessage);
            TrimHistory();

            try
            {
                var provider = BuildProvider();
                // 自発的な話しかけはユーザーが待っているわけではないため、ストリーミング表示はせず
                // 従来通り全文確定後にまとめて表示する(失敗時も静かに何もしない挙動を維持)
                var reply = await provider.ChatStreamAsync(BuildSystemPromptWithMemory(), history, _ => { }, cts.Token).ConfigureAwait(true);
                history.Add(new ChatMessage("assistant", reply));
                PersistMemory();
                voicePlayer.Speak(reply);
                DisplayThinkToSayRnd(reply);
            }
            catch
            {
                history.Remove(userMessage);
            }
            finally
            {
                if (activeRequestCts == cts)
                    activeRequestCts = null;
                cts.Dispose();
            }
        }

        public override void Setting()
        {
            var window = new SettingWindow(plugin.Settings, PetName);
            if (window.ShowDialog() == true)
            {
                plugin.Settings.Save();
                UpdateMicButtonVisibility();
            }
            if (window.MemoryCleared)
            {
                history.Clear();
                longTermSummary = "";
            }
        }

        /// <summary>マイクボタン押下時: 録音の開始/停止をトグルする</summary>
        protected override void OnMicClick(object sender, RoutedEventArgs e)
        {
            if (isRecording)
                StopRecordingAndTranscribe();
            else
                StartRecording();
        }

        private const string MicIdleIcon = "🎙";
        private const string MicRecordingIcon = "⏹";
        private const string MicProcessingIcon = "⏳";

        private const int MicStartBeepHz = 880;
        private const int MicStopBeepHz = 440;
        private const int MicBeepDurationMs = 100;

        /// <summary>設定で有効な場合のみ、マイクの開始/停止を知らせる短いビープ音を鳴らす(UIをブロックしないよう別スレッドで再生)</summary>
        private void PlayMicBeep(int frequencyHz)
        {
            if (!plugin.Settings.MicSoundEnabled)
                return;
            Task.Run(() => Console.Beep(frequencyHz, MicBeepDurationMs));
        }

        private void StartRecording()
        {
            try
            {
                InterruptCurrentReply();
                recorder = new AudioRecorder();
                recorder.Start();
                isRecording = true;
                btnMic.Content = MicRecordingIcon;
                btnMic.ToolTip = "録音を停止";
                PlayMicBeep(MicStartBeepHz);
            }
            catch (Exception ex)
            {
                DisplayThinkToSayRnd($"マイクを開始できませんでした: {ex.Message}");
            }
        }

        private async void StopRecordingAndTranscribe()
        {
            isRecording = false;
            // 録音停止操作が反映されたことが分かるよう、待機中とは別のアイコンで「認識中」を明示する
            btnMic.Content = MicProcessingIcon;
            btnMic.ToolTip = "音声を認識中...";
            btnMic.IsEnabled = false;
            PlayMicBeep(MicStopBeepHz);
            try
            {
                var wav = recorder?.StopAndGetWav();
                recorder = null;
                if (wav == null || wav.Length == 0)
                    return;

                var apiKey = CredentialStore.Load(LLMChatSettings.VoiceInputCredentialKey) ?? string.Empty;
                if (string.IsNullOrEmpty(apiKey))
                {
                    DisplayThinkToSayRnd("音声入力用のOpenAI APIキーが設定されていません(設定画面から登録してください)");
                    return;
                }

                var transcriber = new WhisperTranscriber(apiKey, plugin.Settings.VoiceInputModel);
                var text = await transcriber.TranscribeAsync(wav, CancellationToken.None).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    tbTalk.Text = text.Trim();
                    if (plugin.Settings.VoiceInputAutoSend)
                    {
                        SubmitTalk();
                    }
                    else
                    {
                        tbTalk.CaretIndex = tbTalk.Text.Length;
                        tbTalk.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                DisplayThinkToSayRnd($"音声入力に失敗しました: {ex.Message}");
            }
            finally
            {
                btnMic.Content = MicIdleIcon;
                btnMic.ToolTip = "音声入力";
                btnMic.IsEnabled = true;
            }
        }

        private ILlmChatProvider BuildProvider()
        {
            var apiKey = CredentialStore.Load(plugin.Settings.CredentialKey) ?? string.Empty;
            return ProviderFactory.Create(plugin.Settings, apiKey);
        }

        /// <summary>システムプロンプトに、過去に要約された長期記憶があれば末尾に付け加える</summary>
        private string BuildSystemPromptWithMemory()
        {
            if (string.IsNullOrWhiteSpace(longTermSummary))
                return plugin.Settings.SystemPrompt;
            return $"{plugin.Settings.SystemPrompt}\n\n【あなたが覚えている過去の記憶】\n{longTermSummary}";
        }

        /// <summary>
        /// 上限を超えた古い会話は、丸ごと捨てるのではなくLLMに要約させて長期記憶に積み立てる。
        /// 要約は次回以降の会話に響かないよう、失敗しても静かに諦める(次のTrimHistoryで再度試みられる)
        /// </summary>
        private void TrimHistory()
        {
            if (history.Count <= MaxHistoryMessages)
                return;
            var overflowCount = history.Count - MaxHistoryMessages;
            var overflow = history.GetRange(0, overflowCount);
            history.RemoveRange(0, overflowCount);

            if (plugin.Settings.MemoryPersistenceEnabled)
                _ = ArchiveToLongTermMemoryAsync(overflow);
        }

        private async Task ArchiveToLongTermMemoryAsync(List<ChatMessage> overflow)
        {
            try
            {
                var apiKey = CredentialStore.Load(plugin.Settings.CredentialKey) ?? string.Empty;
                if (string.IsNullOrEmpty(apiKey))
                    return;

                var transcript = string.Join("\n", overflow.Select(m => $"{(m.Role == "user" ? "飼い主" : "キャラクター")}: {m.Content}"));
                var prompt = new List<ChatMessage>
                {
                    new ChatMessage("user",
                        "以下はキャラクターと飼い主のこれまでの会話ログの一部です。今後の会話でキャラクターが覚えておくべき重要な情報" +
                        "(名前・好み・出来事・約束など)だけを、簡潔な日本語の箇条書きで要約してください。些細な雑談は無視して構いません。\n\n" +
                        $"---既存の記憶---\n{longTermSummary}\n\n---新しい会話ログ---\n{transcript}"),
                };

                var provider = ProviderFactory.Create(plugin.Settings, apiKey, maxTokens: 400);
                var summary = await provider.ChatStreamAsync("あなたは会話ログを要約するアシスタントです。", prompt, _ => { }, CancellationToken.None).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(summary))
                    return;

                longTermSummary = summary.Trim();
                var maxChars = plugin.Settings.MemorySummaryMaxChars;
                if (longTermSummary.Length > maxChars)
                    longTermSummary = longTermSummary.Substring(longTermSummary.Length - maxChars);
                PersistMemory();
            }
            catch
            {
                // 要約に失敗しても会話自体は継続できるよう、ここでは無視する
            }
        }

        private void PersistMemory()
        {
            if (!plugin.Settings.MemoryPersistenceEnabled)
                return;
            var store = new ChatMemoryStore { History = new List<ChatMessage>(history), Summary = longTermSummary };
            store.Save(PetName);
        }
    }
}
