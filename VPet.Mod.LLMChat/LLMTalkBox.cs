using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using VPet.Mod.LLMChat.Providers;
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
        private const int MaxHistoryMessages = 40;

        public LLMTalkBox(LLMChatPlugin plugin) : base(plugin)
        {
            this.plugin = plugin;
            // 右クリック等でツールバー(この入力欄を含む)が表示されたら、自動で入力欄にフォーカスする
            IsVisibleChanged += LLMTalkBox_IsVisibleChanged;
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
            DisplayThink();
            history.Add(new ChatMessage("user", text));
            TrimHistory();

            try
            {
                var provider = BuildProvider();
                var reply = await provider.ChatAsync(plugin.Settings.SystemPrompt, history, CancellationToken.None).ConfigureAwait(true);
                history.Add(new ChatMessage("assistant", reply));
                DisplayThinkToSayRnd(reply);
            }
            catch (Exception ex)
            {
                // 失敗した発言は履歴から取り除き、再送信できるようにする
                if (history.Count > 0)
                    history.RemoveAt(history.Count - 1);
                DisplayThinkToSayRnd($"エラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ユーザーの発言なしに、キャラクターから自発的に一言話しかける。
        /// APIキー未設定時や通信エラー時は静かに何もしない(ユーザー操作起点ではないため)。
        /// </summary>
        public async void TriggerProactiveMessage()
        {
            if (!CredentialStore.Exists(plugin.Settings.CredentialKey))
                return;

            DisplayThink();
            history.Add(new ChatMessage("user", "（少し時間が経ちました。あなたから飼い主に一言、自然に話しかけてください。挨拶や近況、思ったことなど、短く自然な一言で構いません。）"));
            TrimHistory();

            try
            {
                var provider = BuildProvider();
                var reply = await provider.ChatAsync(plugin.Settings.SystemPrompt, history, CancellationToken.None).ConfigureAwait(true);
                history.Add(new ChatMessage("assistant", reply));
                DisplayThinkToSayRnd(reply);
            }
            catch
            {
                if (history.Count > 0)
                    history.RemoveAt(history.Count - 1);
            }
        }

        public override void Setting()
        {
            var window = new SettingWindow(plugin.Settings);
            if (window.ShowDialog() == true)
                plugin.Settings.Save();
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
