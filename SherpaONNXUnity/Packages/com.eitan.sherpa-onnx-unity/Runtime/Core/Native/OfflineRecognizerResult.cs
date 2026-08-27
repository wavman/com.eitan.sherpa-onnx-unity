/// Copyright (c)  2024.5 by 东风破

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Eitan.SherpaONNXUnity.Runtime.Native
{
    public class OfflineRecognizerResult
    {
        private const int MaxSupportedResultCount = 1_000_000;

        public OfflineRecognizerResult(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentException("Offline recognizer result handle cannot be null.", nameof(handle));
            }

            Impl impl = (Impl)Marshal.PtrToStructure(handle, typeof(Impl));
            if (impl.Count < 0 || impl.Count > MaxSupportedResultCount)
            {
                throw new InvalidOperationException(
                    $"Offline recognizer result count {impl.Count} is outside the supported range 0-{MaxSupportedResultCount}.");
            }

            _text = ReadUtf8(impl.Text);
            _tokens = ReadTokens(impl.Tokens, impl.Count);
            _timestamps = ReadFloats(impl.Timestamps, impl.Count);
            _durations = ReadFloats(impl.Durations, impl.Count);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Impl
        {
            public IntPtr Text;
            public IntPtr Timestamps;
            public int Count;
            public IntPtr Tokens;
            public IntPtr TokensArr;
            public IntPtr Json;
            public IntPtr Lang;
            public IntPtr Emotion;
            public IntPtr Event;
            public IntPtr Durations;
            public IntPtr YsLogProbs;
            public IntPtr SegmentTimestamps;
            public IntPtr SegmentDurations;
            public IntPtr SegmentTexts;
            public IntPtr SegmentTextsArr;
            public int SegmentCount;
        }

        private readonly string _text;
        public string Text => _text;

        private readonly string[] _tokens;
        public string[] Tokens => _tokens;

        private readonly float[] _timestamps;
        public float[] Timestamps => _timestamps;

        private readonly float[] _durations;
        public float[] Durations => _durations;

        private static string ReadUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return string.Empty;
            }

            int length = 0;
            unsafe
            {
                byte* cursor = (byte*)pointer;
                while (*cursor != 0)
                {
                    cursor++;
                    length++;
                }
            }

            if (length == 0)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[length];
            Marshal.Copy(pointer, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }

        private static string[] ReadTokens(IntPtr pointer, int count)
        {
            if (pointer == IntPtr.Zero || count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[count];
            unsafe
            {
                byte* cursor = (byte*)pointer;
                for (int index = 0; index < count; index++)
                {
                    byte* start = cursor;
                    int length = 0;
                    while (*cursor != 0)
                    {
                        cursor++;
                        length++;
                    }
                    cursor++;

                    if (length == 0)
                    {
                        result[index] = string.Empty;
                        continue;
                    }

                    byte[] buffer = new byte[length];
                    Marshal.Copy((IntPtr)start, buffer, 0, length);
                    result[index] = Encoding.UTF8.GetString(buffer);
                }
            }
            return result;
        }

        private static float[] ReadFloats(IntPtr pointer, int count)
        {
            if (pointer == IntPtr.Zero || count == 0)
            {
                return Array.Empty<float>();
            }

            var result = new float[count];
            Marshal.Copy(pointer, result, 0, count);
            return result;
        }
    }
}
