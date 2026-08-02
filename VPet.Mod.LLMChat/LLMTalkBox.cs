using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using VPet.Mod.LLMChat.Providers;
using VPet.Mod.LLMChat.SpeechToText;
using VPet.Mod.LLMChat.Voice;
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
        private readonly VoicevoxSpeechPlayer voicePlayer;
        private AudioRecorder recorder;
        private bool isRecording;
        private CancellationTokenSource activeRequestCts;
        private const int MaxHistoryMessages = 40;

        public LLMTalkBox(LLMChatPlugin plugin) : base(plugin)
        {
            this.plugin = plugin;
            voicePlayer = new VoicevoxSpeechPlayer(plugin.Settings);
            UpdateMicButtonVisibility();
            // 右クリック等でツールバー(この入力欄を含む)が表示されたら、自動で入力欄にフォーカスする
            IsVisibleChanged += LLMTalkBox_IsVisibleChanged;
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

            try
            {
                var provider = BuildProvider();
                var reply = await provider.ChatAsync(plugin.Settings.SystemPrompt, history, cts.Token).ConfigureAwait(true);
                history.Add(new ChatMessage("assistant", reply));
                voicePlayer.Speak(reply);
                DisplayThinkToSayRnd(reply);
            }
            catch (OperationCanceledException)
            {
                // 新しいメッセージに割り込まれたので、返事を待たずに終わったこの発言は履歴から取り除く
                history.Remove(userMessage);
            }
            catch (Exception ex)
            {
                // 失敗した発言は履歴から取り除き、再送信できるようにする
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
                var reply = await provider.ChatAsync(plugin.Settings.SystemPrompt, history, cts.Token).ConfigureAwait(true);
                history.Add(new ChatMessage("assistant", reply));
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
            var window = new SettingWindow(plugin.Settings);
            if (window.ShowDialog() == true)
            {
                plugin.Settings.Save();
                UpdateMicButtonVisibility();
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

        private void StartRecording()
        {
            try
            {
                InterruptCurrentReply();
                recorder = new AudioRecorder();
                recorder.Start();
                isRecording = true;
                btnMic.Content = "⏹";
            }
            catch (Exception ex)
            {
                DisplayThinkToSayRnd($"マイクを開始できませんでした: {ex.Message}");
            }
        }

        private async void StopRecordingAndTranscribe()
        {
            isRecording = false;
            btnMic.Content = "🎙";
            btnMic.IsEnabled = false;
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
                btnMic.IsEnabled = true;
            }
        }

        private ILlmChatProvider BuildProvider()
        {
            var apiKey = CredentialStore.Load(plugin.Settings.CredentialKey) ?? string.Empty;
            return ProviderFactory.Create(plugin.Settings, apiKey);
        }

        private void TrimHistory()
        {
            while (history.Count > MaxHistoryMessages)
                history.RemoveAt(0);
        }
    }
}
