using System;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace BetterFG.Features.Replay
{
    internal class ReplayEncoder : IDisposable
    {
        const long Hns = 10000000L;
        const int WavHeader = 44;
        const int AudioBytesPerSecond = ReplayProcessAudioCapture.SampleRate * ReplayProcessAudioCapture.Channels * (ReplayProcessAudioCapture.Bits / 8);

        readonly IMFSinkWriter _writer;
        readonly int _width;
        readonly int _height;
        readonly int _stride;
        readonly long _frameDuration;
        readonly int _videoStream;
        readonly int _audioStream = -1;
        readonly byte[] _audioChunk;

        FileStream _wav;
        long _frames;
        long _audioTime;
        bool _closed;

        public ReplayEncoder(string path, int width, int height, int fps, int kbps, string wavPath, float wavLead)
        {
            MediaFactory.MFStartup(false);

            _width = width;
            _height = height;
            _stride = width * 4;
            _frameDuration = Hns / fps;

            _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, null);

            var videoOut = MediaFactory.MFCreateMediaType();
            videoOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            videoOut.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            videoOut.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(kbps * 1000));
            videoOut.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            MediaFactory.MFSetAttributeSize(videoOut, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
            MediaFactory.MFSetAttributeRatio(videoOut, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
            MediaFactory.MFSetAttributeRatio(videoOut, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
            _videoStream = _writer.AddStream(videoOut);

            var videoIn = MediaFactory.MFCreateMediaType();
            videoIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            videoIn.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            videoIn.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            MediaFactory.MFSetAttributeSize(videoIn, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
            MediaFactory.MFSetAttributeRatio(videoIn, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
            MediaFactory.MFSetAttributeRatio(videoIn, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
            _writer.SetInputMediaType(_videoStream, videoIn, null);

            if (!string.IsNullOrEmpty(wavPath) && File.Exists(wavPath))
            {
                var audioOut = MediaFactory.MFCreateMediaType();
                audioOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                audioOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                audioOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, (uint)ReplayProcessAudioCapture.Bits);
                audioOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)ReplayProcessAudioCapture.SampleRate);
                audioOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)ReplayProcessAudioCapture.Channels);
                audioOut.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u);
                _audioStream = _writer.AddStream(audioOut);

                var audioIn = MediaFactory.MFCreateMediaType();
                audioIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                audioIn.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                audioIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, (uint)ReplayProcessAudioCapture.Bits);
                audioIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)ReplayProcessAudioCapture.SampleRate);
                audioIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)ReplayProcessAudioCapture.Channels);
                audioIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(ReplayProcessAudioCapture.Channels * (ReplayProcessAudioCapture.Bits / 8)));
                audioIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)AudioBytesPerSecond);
                _writer.SetInputMediaType(_audioStream, audioIn, null);

                _audioChunk = new byte[AudioBytesPerSecond / fps / 4 * 4 + 4];
                _wav = File.OpenRead(wavPath);

                long skip = WavHeader + (long)(wavLead * AudioBytesPerSecond);
                skip -= skip % 4;
                if (skip > 0 && skip < _wav.Length) _wav.Position = skip;
            }

            _writer.BeginWriting();
        }

        public void WriteFrame(byte[] bgra)
        {
            int size = _stride * _height;
            var buffer = MediaFactory.MFCreateMemoryBuffer(size);

            buffer.Lock(out IntPtr ptr, out _, out _);
            Marshal.Copy(bgra, 0, ptr, size);
            buffer.Unlock();
            buffer.CurrentLength = size;

            var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = _frames * _frameDuration;
            sample.SampleDuration = _frameDuration;
            _writer.WriteSample(_videoStream, sample);

            sample.Dispose();
            buffer.Dispose();
            _frames++;

            PumpAudio();
        }

        void PumpAudio()
        {
            long until = _frames * _frameDuration;
            while (_wav != null && _audioTime < until)
            {
                int read = _wav.Read(_audioChunk, 0, _audioChunk.Length);
                read -= read % 4;
                if (read <= 0)
                {
                    _wav.Dispose();
                    _wav = null;
                    return;
                }

                var buffer = MediaFactory.MFCreateMemoryBuffer(read);
                buffer.Lock(out IntPtr ptr, out _, out _);
                Marshal.Copy(_audioChunk, 0, ptr, read);
                buffer.Unlock();
                buffer.CurrentLength = read;

                long duration = read * Hns / AudioBytesPerSecond;

                var sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = _audioTime;
                sample.SampleDuration = duration;
                _writer.WriteSample(_audioStream, sample);

                sample.Dispose();
                buffer.Dispose();
                _audioTime += duration;
            }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;

            if (_wav != null) { _wav.Dispose(); _wav = null; }

            _writer.Finalize();
            _writer.Dispose();
            MediaFactory.MFShutdown();
        }
    }
}
