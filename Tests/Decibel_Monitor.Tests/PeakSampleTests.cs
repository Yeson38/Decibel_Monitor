using System;
using Decibel_Monitor.Services;

namespace Decibel_Monitor.Tests;

public class PeakSampleTests
{
    // ---- 16-bit PCM ----

    [Fact]
    public void ComputePeak_16BitPcm_Should_FindMaximumAbsoluteSample()
    {
        var buffer = new byte[6];
        // 两个 16-bit 采样：0x1000 (4096) 与 -0x4000 (-16384)
        WriteInt16(buffer, 0, 4096);
        WriteInt16(buffer, 2, -16384);
        WriteInt16(buffer, 4, 0);

        var peak = PeakSample.ComputePeak(buffer, buffer.Length, false, 16);

        Assert.Equal(0.5f, peak, 5); // 16384 / 32768
    }

    [Fact]
    public void ComputePeak_16BitPcm_Should_HandleFullScale()
    {
        var buffer = new byte[2];
        WriteInt16(buffer, 0, short.MinValue); // -32768

        var peak = PeakSample.ComputePeak(buffer, buffer.Length, false, 16);

        Assert.Equal(1.0f, peak, 5);
    }

    // ---- 32-bit float ----

    [Fact]
    public void ComputePeak_IeeeFloat_Should_FindMaximumAbsoluteSample()
    {
        var buffer = new byte[8];
        BitConverter.GetBytes(-0.25f).CopyTo(buffer, 0);
        BitConverter.GetBytes(1.0f).CopyTo(buffer, 4);

        var peak = PeakSample.ComputePeak(buffer, buffer.Length, true, 32);

        Assert.Equal(1.0f, peak, 5);
    }

    // ---- 32-bit PCM ----

    [Fact]
    public void ComputePeak_32BitPcm_Should_FindMaximumAbsoluteSample()
    {
        var buffer = new byte[8];
        BitConverter.GetBytes(0x20000000).CopyTo(buffer, 0); // 536870912
        BitConverter.GetBytes(unchecked((int)0x80000000)).CopyTo(buffer, 4); // int.MinValue

        var peak = PeakSample.ComputePeak(buffer, buffer.Length, false, 32);

        Assert.Equal(1.0f, peak, 5);
    }

    // ---- 边界 ----

    [Fact]
    public void ComputePeak_EmptyOrNull_Should_ReturnZero()
    {
        Assert.Equal(0f, PeakSample.ComputePeak(Array.Empty<byte>(), 0, false, 16));
        Assert.Equal(0f, PeakSample.ComputePeak(new byte[] { 1, 2 }, 0, false, 16));
    }

    [Fact]
    public void ComputePeak_UnknownFormat_Should_ReturnZero()
    {
        // 24-bit 等未支持格式不应抛异常
        var buffer = new byte[3];
        var peak = PeakSample.ComputePeak(buffer, buffer.Length, false, 24);
        Assert.Equal(0f, peak);
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
