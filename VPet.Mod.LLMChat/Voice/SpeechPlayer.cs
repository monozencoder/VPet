using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VPet.Mod.LLMChat.Voice
{
    /// <summary>
    /// 設定されたプロバイダー(VOICEVOX/OpenAI/A.I.VOICE)でテキストを音声合成し再生する。
    /// 音声はあくまで補助機能のため、エンジン未起動や通信エラーが発生してもチャット自体は止めず、静かに諦める。
    /// </summary>
    public class SpeechPlayer
    {
        private readonly LLMChatSettings settings;
        private readonly object playbackLock = new object();
        private WaveOutEvent currentOutput;
        private WaveStream currentWaveStream;
        private CancellationTokenSource currentCts;

        /// <summary>再生音量(0.0～1.0)。試し読み(設定がnull/未反映のとき)はこの値を直接調整して使う</summary>
        public float Volume { get; set; } = 1f;

        public SpeechPlayer(LLMChatSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>設定で有効な場合、直前の再生を止めて新しいテキストを読み上げる</summary>
        public void Speak(string text)
        {
            if (settings == null || !settings.VoiceEnabled || string.IsNullOrWhiteSpace(text))
                return;

            Volume = Math.Clamp(settings.VoiceVolumePercent / 100f, 0f, 1f);

            switch (settings.TtsProvider)
            {
                case TtsProviderKind.OpenAi:
                    var apiKey = CredentialStore.Load(LLMChatSettings.OpenAiTtsCredentialKey) ?? string.Empty;
                    SpeakWith(text, ct => new OpenAiTtsClient().SynthesizeAsync(text, apiKey, settings.OpenAiTtsModel, settings.OpenAiTtsVoice, ct));
                    break;
                case TtsProviderKind.AiVoice:
                    SpeakWith(text, ct => new AiVoiceClient(settings.AiVoiceInstallDir).SynthesizeAsync(text, settings.AiVoicePresetName, ct));
                    break;
                default:
                    SpeakWith(text, ct => new VoicevoxClient(settings.VoiceEndpoint).SynthesizeAsync(text, settings.VoiceSpeakerId, ct));
                    break;
            }
        }

        /// <summary>設定の有効フラグに関わらず、指定内容でVOICEVOXの試し読みをする(設定画面のテストボタン用)。結果はonSuccess/onErrorに渡る</summary>
        public void SpeakForTestVoicevox(string text, string endpoint, int speakerId, Action onSuccess = null, Action<string> onError = null) =>
            SpeakWith(text, ct => new VoicevoxClient(endpoint).SynthesizeAsync(text, speakerId, ct), onSuccess, onError);

        /// <summary>設定の有効フラグに関わらず、指定内容でOpenAI TTSの試し読みをする(設定画面のテストボタン用)。結果はonSuccess/onErrorに渡る</summary>
        public void SpeakForTestOpenAi(string text, string model, string voice, string apiKey, Action onSuccess = null, Action<string> onError = null) =>
            SpeakWith(text, ct => new OpenAiTtsClient().SynthesizeAsync(text, apiKey, model, voice, ct), onSuccess, onError);

        /// <summary>設定の有効フラグに関わらず、指定内容でA.I.VOICEの試し読みをする(設定画面のテストボタン用)。結果はonSuccess/onErrorに渡る</summary>
        public void SpeakForTestAiVoice(string text, string installDir, string presetName, Action onSuccess = null, Action<string> onError = null) =>
            SpeakWith(text, ct => new AiVoiceClient(installDir).SynthesizeAsync(text, presetName, ct), onSuccess, onError);

        private void SpeakWith(string text, Func<CancellationToken, Task<byte[]>> synthesize, Action onSuccess = null, Action<string> onError = null)
        {
            Stop();
            var cts = new CancellationTokenSource();
            currentCts = cts;
            _ = SpeakAsync(synthesize, onSuccess, onError, cts.Token);
        }

        private async Task SpeakAsync(Func<CancellationToken, Task<byte[]>> synthesize, Action onSuccess, Action<string> onError, CancellationToken cancellationToken)
        {
            try
            {
                var wav = await synthesize(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    return;
                Play(wav);
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                // 通常の読み上げ(Speak)ではエンジン未起動・接続失敗等でチャット自体を止めたくないため黙って諦めるが、
                // 試し読み(テストボタン)はエラー原因を確認する目的なのでonError経由で呼び出し元に伝える
                onError?.Invoke(ex.Message);
            }
        }

        private void Play(byte[] wav)
        {
            // System.Media.SoundPlayer(winmm PlaySound)はOpenAI等が返すwavの微妙な形式差異で
            // 例外を投げずに無音のまま失敗することがあるため、より寛容なNAudioで再生する
            var waveStream = new WaveFileReader(new MemoryStream(wav));
            var output = new WaveOutEvent();
            output.Init(waveStream);
            output.Volume = Math.Clamp(Volume, 0f, 1f);

            lock (playbackLock)
            {
                currentOutput?.Stop();
                currentOutput?.Dispose();
                currentWaveStream?.Dispose();
                currentOutput = output;
                currentWaveStream = waveStream;
            }
            output.Play();
        }

        public void Stop()
        {
            currentCts?.Cancel();
            lock (playbackLock)
            {
                currentOutput?.Stop();
            }
        }
    }
}
