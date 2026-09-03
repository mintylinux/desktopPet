using System;
using System.Diagnostics;
using System.IO;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Plays short MP3 sound effects (as embedded in the pet's animations.xml) by shelling out
    /// to ffplay. Replaces the original NAudio.Wave.WaveOut/Mp3FileReader usage, since NAudio's
    /// playback backends (WinMM/DirectSound/WASAPI) are Windows-only.
    /// </summary>
    public sealed class AudioPlayer : IDisposable
    {
        private readonly string _tempFile;
        private int _loopsRemaining;
        private double _volume = 1.0;
        private Process? _current;

        /// <summary>
        /// Writes the sound's MP3 bytes to a temp file once, ready to be played (possibly many
        /// times) via ffplay.
        /// </summary>
        public AudioPlayer(byte[] mp3Bytes)
        {
            _tempFile = Path.Combine(Path.GetTempPath(), "desktoppet_sfx_" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllBytes(_tempFile, mp3Bytes);
        }

        /// <summary>Volume from 0.0 (silent) to 1.0 (full).</summary>
        public double Volume
        {
            get => _volume;
            set => _volume = Math.Clamp(value, 0.0, 1.0);
        }

        /// <summary>
        /// Plays the sound once, optionally looping <paramref name="loopCount"/> additional times
        /// after it finishes (mirrors the original TSound.Play/PlaybackStopped loop behavior).
        /// </summary>
        public void Play(int loopCount = 0)
        {
            if (_volume <= 0.0) return;
            _loopsRemaining = loopCount;
            PlayOnce();
        }

        private void PlayOnce()
        {
            try
            {
                // -nodisp: no video window, -autoexit: quit when playback ends,
                // -loglevel quiet: no console spam, -volume: 0-100 scale.
                int volumePercent = (int)(_volume * 100);
                var psi = new ProcessStartInfo
                {
                    FileName = "ffplay",
                    Arguments = $"-nodisp -autoexit -loglevel quiet -volume {volumePercent} \"{_tempFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                _current = Process.Start(psi);
                if (_current != null)
                {
                    _current.EnableRaisingEvents = true;
                    _current.Exited += OnExited;
                }
            }
            catch (Exception)
            {
                // No ffplay available or playback failed; silently give up on this sound.
            }
        }

        private void OnExited(object? sender, EventArgs e)
        {
            if (_loopsRemaining-- > 0)
            {
                PlayOnce();
            }
        }

        public void Dispose()
        {
            try
            {
                if (_current is { HasExited: false })
                {
                    _current.Kill();
                }
            }
            catch (Exception)
            {
                // ignore
            }
            try
            {
                File.Delete(_tempFile);
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
