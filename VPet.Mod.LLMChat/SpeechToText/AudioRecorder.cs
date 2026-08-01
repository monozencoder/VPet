using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace VPet.Mod.LLMChat.SpeechToText
{
    /// <summary>
    /// マイクからの録音。Whisper APIに適した16kHz/モノラル/16bit PCMのWAVを生成する。
    /// </summary>
    public class AudioRecorder : IDisposable
    {
        private WaveInEvent waveIn;
        private MemoryStream buffer;
        private WaveFileWriter writer;
        private readonly ManualResetEventSlim stopped = new ManualResetEventSlim(false);

        public void Start()
        {
            stopped.Reset();
            buffer = new MemoryStream();
            waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
            writer = new WaveFileWriter(buffer, waveIn.WaveFormat);
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;
            waveIn.StartRecording();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            stopped.Set();
        }

        /// <summary>録音を停止し、WAVファイルのバイト列を返す</summary>
        public byte[] StopAndGetWav()
        {
            if (waveIn == null)
                return null;

            waveIn.StopRecording();
            stopped.Wait(TimeSpan.FromSeconds(2));
            writer.Flush();
            var result = buffer.ToArray();
            Dispose();
            return result;
        }

        public void Dispose()
        {
            if (waveIn != null)
            {
                waveIn.DataAvailable -= WaveIn_DataAvailable;
                waveIn.RecordingStopped -= WaveIn_RecordingStopped;
                waveIn.Dispose();
                waveIn = null;
            }
            writer?.Dispose();
            writer = null;
        }
    }
}
