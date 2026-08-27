using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Eitan.SherpaONNXUnity.Runtime.Native;
using NUnit.Framework;

namespace Eitan.SherpaONNXUnity.Tests
{
    public sealed class OfflineRecognizerResultAbiTests
    {
        private const int MaxSupportedResultCount = 1_000_000;
        private const int Layout32TokensArrayOffset = 16;
        private const int Layout32DurationsOffset = 36;
        private const int Layout32SegmentCountOffset = 60;
        private const int Layout32Size = 64;
        private const int Layout64TokensArrayOffset = 32;
        private const int Layout64DurationsOffset = 72;
        private const int Layout64SegmentCountOffset = 120;
        private const int Layout64Size = 128;

        private static int CurrentTokensArrayOffset =>
            IntPtr.Size == 8 ? Layout64TokensArrayOffset : Layout32TokensArrayOffset;
        private static int CurrentDurationsOffset =>
            IntPtr.Size == 8 ? Layout64DurationsOffset : Layout32DurationsOffset;
        private static int CurrentSegmentCountOffset =>
            IntPtr.Size == 8 ? Layout64SegmentCountOffset : Layout32SegmentCountOffset;
        private static int CurrentLayoutSize => IntPtr.Size == 8 ? Layout64Size : Layout32Size;

        [Test]
        public void NativeLayout_MatchesPinnedSherpaOfflineResultOnCurrentArchitecture()
        {
            Type layout = typeof(OfflineRecognizerResult).GetNestedType(
                "Impl",
                BindingFlags.NonPublic);

            Assert.That(layout, Is.Not.Null);
            Assert.That(Marshal.OffsetOf(layout, "Text").ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf(layout, "Timestamps").ToInt32(), Is.EqualTo(IntPtr.Size));
            Assert.That(Marshal.OffsetOf(layout, "Count").ToInt32(), Is.EqualTo(IntPtr.Size * 2));
            Assert.That(Marshal.OffsetOf(layout, "Tokens").ToInt32(), Is.EqualTo(IntPtr.Size == 8 ? 24 : 12));
            Assert.That(Marshal.OffsetOf(layout, "TokensArr").ToInt32(), Is.EqualTo(CurrentTokensArrayOffset));
            Assert.That(Marshal.OffsetOf(layout, "Durations").ToInt32(), Is.EqualTo(CurrentDurationsOffset));
            Assert.That(Marshal.OffsetOf(layout, "SegmentCount").ToInt32(), Is.EqualTo(CurrentSegmentCountOffset));
            Assert.That(Marshal.SizeOf(layout), Is.EqualTo(CurrentLayoutSize));
        }

        [TestCase(typeof(OfflineResultLayout32), Layout32TokensArrayOffset, Layout32DurationsOffset, Layout32SegmentCountOffset, Layout32Size)]
        [TestCase(typeof(OfflineResultLayout64), Layout64TokensArrayOffset, Layout64DurationsOffset, Layout64SegmentCountOffset, Layout64Size)]
        public void PortableReferenceLayouts_EncodePinnedX86AndX64Contracts(
            Type layout,
            int tokensArrayOffset,
            int durationsOffset,
            int segmentCountOffset,
            int size)
        {
            Assert.That(Marshal.OffsetOf(layout, "TokensArr").ToInt32(), Is.EqualTo(tokensArrayOffset));
            Assert.That(Marshal.OffsetOf(layout, "Durations").ToInt32(), Is.EqualTo(durationsOffset));
            Assert.That(Marshal.OffsetOf(layout, "SegmentCount").ToInt32(), Is.EqualTo(segmentCountOffset));
            Assert.That(Marshal.SizeOf(layout), Is.EqualTo(size));
        }

        [Test]
        public void Constructor_ReadsDurationPointerInsteadOfTokensArrayPointer()
        {
            using var native = new OfflineResultFixture(
                text: "中文 result",
                tokens: new[] { "中文", "result" },
                timestamps: new[] { 0.25f, 0.75f },
                durations: new[] { 0.5f, 1.25f });

            var result = new OfflineRecognizerResult(native.Result);

            Assert.That(result.Text, Is.EqualTo("中文 result"));
            Assert.That(result.Tokens, Is.EqualTo(new[] { "中文", "result" }));
            Assert.That(result.Timestamps, Is.EqualTo(new[] { 0.25f, 0.75f }));
            Assert.That(result.Durations, Is.EqualTo(new[] { 0.5f, 1.25f }));
        }

        [Test]
        public void Constructor_MapsMissingOptionalArraysToEmptyArrays()
        {
            using var native = new OfflineResultFixture(
                text: string.Empty,
                tokens: Array.Empty<string>(),
                timestamps: null,
                durations: null);

            var result = new OfflineRecognizerResult(native.Result);

            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Tokens, Is.Empty);
            Assert.That(result.Timestamps, Is.Empty);
            Assert.That(result.Durations, Is.Empty);
        }

        [TestCase(-1)]
        [TestCase(MaxSupportedResultCount + 1)]
        public void Constructor_RejectsCorruptNativeCountBeforeAllocatingArrays(int count)
        {
            int size = CurrentLayoutSize;
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Zero(memory, size);
                Marshal.WriteInt32(memory, IntPtr.Size * 2, count);

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    new OfflineRecognizerResult(memory));

                Assert.That(error.Message, Does.Contain(count.ToString()));
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        private sealed class OfflineResultFixture : IDisposable
        {
            private readonly List<IntPtr> allocations = new List<IntPtr>();

            public OfflineResultFixture(
                string text,
                string[] tokens,
                float[] timestamps,
                float[] durations)
            {
                int size = CurrentLayoutSize;
                Result = Allocate(size);
                WritePointer(Result, 0, AllocateUtf8(text));
                WritePointer(Result, IntPtr.Size, AllocateFloats(timestamps));
                Marshal.WriteInt32(Result, IntPtr.Size * 2, tokens?.Length ?? 0);
                WritePointer(Result, IntPtr.Size == 8 ? 24 : 12, AllocateTokens(tokens));

                // Deliberately non-null: the stale wrapper read this field as Durations.
                WritePointer(
                    Result,
                    CurrentTokensArrayOffset,
                    Allocate(IntPtr.Size * Math.Max(1, tokens?.Length ?? 0)));
                WritePointer(Result, CurrentDurationsOffset, AllocateFloats(durations));
            }

            public IntPtr Result { get; }

            public void Dispose()
            {
                foreach (IntPtr allocation in allocations)
                {
                    Marshal.FreeHGlobal(allocation);
                }
            }

            private IntPtr Allocate(int size)
            {
                IntPtr value = Marshal.AllocHGlobal(Math.Max(1, size));
                allocations.Add(value);
                Zero(value, Math.Max(1, size));
                return value;
            }

            private IntPtr AllocateUtf8(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
                IntPtr memory = Allocate(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                return memory;
            }

            private IntPtr AllocateTokens(string[] values)
            {
                if (values == null || values.Length == 0) return IntPtr.Zero;
                byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\0", values) + "\0");
                IntPtr memory = Allocate(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                return memory;
            }

            private IntPtr AllocateFloats(float[] values)
            {
                if (values == null || values.Length == 0) return IntPtr.Zero;
                IntPtr memory = Allocate(sizeof(float) * values.Length);
                Marshal.Copy(values, 0, memory, values.Length);
                return memory;
            }

            private static void WritePointer(IntPtr target, int offset, IntPtr value) =>
                Marshal.WriteIntPtr(target, offset, value);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct OfflineResultLayout32
        {
            public uint Text;
            public uint Timestamps;
            public int Count;
            public uint Tokens;
            public uint TokensArr;
            public uint Json;
            public uint Lang;
            public uint Emotion;
            public uint Event;
            public uint Durations;
            public uint YsLogProbs;
            public uint SegmentTimestamps;
            public uint SegmentDurations;
            public uint SegmentTexts;
            public uint SegmentTextsArr;
            public int SegmentCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct OfflineResultLayout64
        {
            public ulong Text;
            public ulong Timestamps;
            public int Count;
            public ulong Tokens;
            public ulong TokensArr;
            public ulong Json;
            public ulong Lang;
            public ulong Emotion;
            public ulong Event;
            public ulong Durations;
            public ulong YsLogProbs;
            public ulong SegmentTimestamps;
            public ulong SegmentDurations;
            public ulong SegmentTexts;
            public ulong SegmentTextsArr;
            public int SegmentCount;
        }

        private static void Zero(IntPtr memory, int size)
        {
            Marshal.Copy(new byte[size], 0, memory, size);
        }
    }
}
