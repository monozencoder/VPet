using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// VOICEVOXでテキストを音声合成し再生する。
    /// 音声はあくまで補助機能のため、VOICEVOX未起動や通信エラーが発生してもチャット自体は止めず、静かに諦める。
    /// </summary>
    public class VoicevoxSpeechPlayer
    {
        private readonly LLMChatSettings settings;
        private readonly object playbackLock = new object();
        private SoundPlayer currentPlayer;
        private MemoryStream currentStream;
        private CancellationTokenSource currentCts;

        public VoicevoxSpeechPlayer(LLMChatSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>設定で有効な場合、直前の再生を止めて新しいテキストを読み上げる</summary>
        public void Speak(string text)
        {
            if (settings == null || !settings.VoiceEnabled || string.IsNullOrWhiteSpace(text))
                return;
            SpeakWith(text, settings.VoiceEndpoint, settings.VoiceSpeakerId);
        }

        /// <summary>設定の有効フラグに関わらず、指定内容で試し読みする(設定画面のテストボタン用)</summary>
        public void SpeakForTest(string text, string endpoint, int speakerId)
        {
            SpeakWith(text, endpoint, speakerId);
        }

        private void SpeakWith(string text, string endpoint, int speakerId)
        {
            Stop();
            var cts = new CancellationTokenSource();
            currentCts = cts;
            _ = SpeakAsync(text, endpoint, speakerId, cts.Token);
        }

        private async Task SpeakAsync(string text, string endpoint, int speakerId, CancellationToken cancellationToken)
        {
            try
            {
                var client = new VoicevoxClient(endpoint);
                var wav = await client.SynthesizeAsync(text, speakerId, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    return;
                Play(wav);
            }
            catch
            {
                // VOICEVOX未起動・接続失敗・合成エラー等は読み上げを諦めるだけにする
            }
        }

        private void Play(byte[] wav)
        {
            var stream = new MemoryStream(wav);
            var player = new SoundPlayer(stream);
            player.Load();

            lock (playbackLock)
            {
                currentPlayer?.Stop();
                currentPlayer?.Dispose();
                currentStream?.Dispose();
                currentPlayer = player;
                currentStream = stream;
            }
            player.Play();
        }

        public void Stop()
        {
            currentCts?.Cancel();
            lock (playbackLock)
            {
                currentPlayer?.Stop();
            }
        }
    }
}
