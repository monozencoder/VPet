using System;
using System.Timers;
using VPet_Simulator.Windows.Interface;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// LLMChat MOD 本体。Claude / ChatGPT(OpenAI) / DeepSeek / カスタムAPIでキャラクターと会話する。
    /// </summary>
    public class LLMChatPlugin : MainPlugin
    {
        public override string PluginName => "LLMChat";

        /// <summary>非秘匿設定(APIキーはCredentialStore側で管理)</summary>
        public LLMChatSettings Settings { get; private set; }

        private LLMTalkBox talkBox;
        private readonly Random rnd = new Random();
        private DateTime lastProactiveCheck = DateTime.Now;

        public LLMChatPlugin(IMainWindow mainwin) : base(mainwin)
        {
        }

        public override void LoadPlugin()
        {
            Settings = LLMChatSettings.Load();
            talkBox = new LLMTalkBox(this);
            MW.TalkAPI.Add(talkBox);

            // 一定間隔ごとに、設定に応じた確率でキャラクターから自発的に話しかける
            MW.Main.EventTimer.Elapsed += OnEventTimerElapsed;
        }

        private void OnEventTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (Settings == null || !Settings.ProactiveChatEnabled)
                return;
            if (DateTime.Now - lastProactiveCheck < TimeSpan.FromMinutes(Math.Max(1, Settings.ProactiveChatIntervalMinutes)))
                return;
            lastProactiveCheck = DateTime.Now;

            if (rnd.Next(100) >= Settings.ProactiveChatChancePercent)
                return;

            talkBox?.TriggerProactiveMessage();
        }

        public override void Save()
        {
            Settings?.Save();
        }
    }
}
