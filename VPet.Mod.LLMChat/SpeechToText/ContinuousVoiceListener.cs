using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace VPet.Mod.LLMChat.SpeechToText
{
    /// <summary>
    /// ハンズフリー会話モード用: マイクを常時開いたままにし、音量による簡易VAD(発話区間検出)で
    /// 発話の開始/終了を自動検出して1発言ごとにWAVを切り出す。
    /// Pause()中(キャラクターの発話再生中やユーザーによるミュート中)は入力を無視し、
    /// マイクデバイス自体は開いたままにすることで再開時のラグを無くす。
    /// </summary>
    public class ContinuousVoiceListener : IDisposable
    {
        private readonly LLMChatSettings settings;
        private readonly WaveFormat format = new WaveFormat(16000, 16, 1);
        private WaveInEvent waveIn;
        private MemoryStream segmentBuffer;
        private WaveFileWriter segmentWriter;
        private readonly Queue<byte[]> preRoll = new Queue<byte[]>();
        private const int PreRollChunks = 2;

        private bool isSpeaking;
        private double speechMs;
        private double silenceMs;

        /// <summary>ユーザーが1発言話し終えたときに、切り出したWAVバイト列とともに発火する</summary>
        public event Action<byte[]> SegmentReady;
        /// <summary>聞き取り状態(待機中/発話検出中)が変わったときに発火する(UIのアイコン更新用)</summary>
        public event Action ListeningStateChanged;

        public bool IsPaused { get; private set; }
        public bool IsSpeechActive => isSpeaking;

        public ContinuousVoiceListener(LLMChatSettings settings)
        {
            this.settings = settings;
        }

        public void Start()
        {
            if (waveIn != null)
                return;
            waveIn = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100 };
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.StartRecording();
        }

        /// <summary>入力の処理を一時停止する(デバイスは開いたまま)</summary>
        public void Pause()
        {
            IsPaused = true;
            ResetSegment();
            preRoll.Clear();
        }

        public void Resume() => IsPaused = false;

        public void Stop()
        {
            if (waveIn == null)
                return;
            waveIn.DataAvailable -= WaveIn_DataAvailable;
            waveIn.StopRecording();
            waveIn.Dispose();
            waveIn = null;
            ResetSegment();
            preRoll.Clear();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (IsPaused || e.BytesRecorded == 0)
                return;

            var chunk = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, chunk, e.BytesRecorded);
            var amplitude = CalculateNormalizedRms(chunk);
            var chunkMs = chunk.Length / (double)format.AverageBytesPerSecond * 1000.0;
            var isLoud = amplitude >= settings.HandsFreeVolumeThreshold;

            if (!isSpeaking)
            {
                if (isLoud)
                {
                    isSpeaking = true;
                    speechMs = 0;
                    silenceMs = 0;
                    segmentBuffer = new MemoryStream();
                    segmentWriter = new WaveFileWriter(segmentBuffer, format);
                    // 発話開始直前の音を含めて自然な出だしにする
                    foreach (var preChunk in preRoll)
                        segmentWriter.Write(preChunk, 0, preChunk.Length);
                    preRoll.Clear();
                    segmentWriter.Write(chunk, 0, chunk.Length);
                    speechMs += chunkMs;
                    ListeningStateChanged?.Invoke();
                }
                else
                {
                    preRoll.Enqueue(chunk);
                    while (preRoll.Count > PreRollChunks)
                        preRoll.Dequeue();
                }
                return;
            }

            segmentWriter.Write(chunk, 0, chunk.Length);
            speechMs += chunkMs;
            if (isLoud)
            {
                silenceMs = 0;
                return;
            }

            silenceMs += chunkMs;
            if (silenceMs >= settings.HandsFreeSilenceMs && speechMs >= settings.HandsFreeMinSpeechMs)
                FinalizeSegment();
        }

        private void FinalizeSegment()
        {
            segmentWriter.Flush();
            var wav = segmentBuffer.ToArray();
            ResetSegment();
            ListeningStateChanged?.Invoke();
            SegmentReady?.Invoke(wav);
        }

        private void ResetSegment()
        {
            segmentWriter?.Dispose();
            segmentWriter = null;
            segmentBuffer = null;
            isSpeaking = false;
            speechMs = 0;
            silenceMs = 0;
        }

        private static double CalculateNormalizedRms(byte[] buffer)
        {
            var sampleCount = buffer.Length / 2;
            if (sampleCount == 0)
                return 0;
            long sumSquares = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(buffer, i * 2);
                sumSquares += (long)sample * sample;
            }
            var rms = Math.Sqrt(sumSquares / (double)sampleCount);
            return rms / short.MaxValue;
        }

        public void Dispose() => Stop();
    }
}
