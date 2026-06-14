using System;
using NAudio.Wave;

namespace clicky_windows
{
    public class AudioRecorder : IDisposable
    {
        private WaveInEvent? _waveIn;
        public event Action<byte[]>? DataAvailable;
        public event Action<double>? PowerLevelChanged;

        public event Action<Exception?>? RecordingStopped;

        public bool IsRecording { get; private set; }

        public void Start()
        {
            if (IsRecording) return;

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono PCM
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsRecording = true;
                Console.WriteLine("🎙️ Microphones recording started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to start audio recording: {ex.Message}");
                RecordingStopped?.Invoke(ex);
            }
        }

        public void Stop()
        {
            if (!IsRecording || _waveIn == null) return;

            try
            {
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
                IsRecording = false;
                PowerLevelChanged?.Invoke(0);
                Console.WriteLine("🎙️ Microphone recording stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to stop audio recording: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            IsRecording = false;
            PowerLevelChanged?.Invoke(0);
            RecordingStopped?.Invoke(e.Exception);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // Send raw PCM16 bytes
            byte[] data = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, data, e.BytesRecorded);
            DataAvailable?.Invoke(data);

            // Calculate power level (RMS)
            double sum = 0;
            int sampleCount = e.BytesRecorded / 2;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                sum += sample * sample;
            }

            double rms = Math.Sqrt(sum / sampleCount);
            // Normalize to a 0.0 - 1.0 range
            double level = Math.Min(1.0, rms / 32768.0 * 5.0); // Amplified slightly for visual effect
            PowerLevelChanged?.Invoke(level);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
