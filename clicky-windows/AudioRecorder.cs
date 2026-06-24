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
                // Use device index 0 (the default capture device). If no mic is present
                // or the default device is unavailable, StartRecording throws here and we
                // log the full exception -- this is the most likely cause of the waveform
                // dying instantly on hotkey press.
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16-bit, mono PCM
                    DeviceNumber = 0
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsRecording = true;
                Logger.Info("AudioRecorder: recording started on device 0 (default)");
            }
            catch (Exception ex)
            {
                Logger.Error("AudioRecorder: StartRecording threw (mic device unavailable?)", ex);
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
                Logger.Info("AudioRecorder: recording stopped normally");
            }
            catch (Exception ex)
            {
                Logger.Error("AudioRecorder: Stop threw", ex);
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
