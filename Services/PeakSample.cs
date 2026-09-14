using System;

namespace Decibel_Monitor.Services;

/// <summary>
/// 音频峰值解析纯函数工具类（不依赖 NAudio 具体类型，便于单元测试）。
/// </summary>
public static class PeakSample
{
    /// <summary>
    /// 从 PCM/浮点字节缓冲区中计算最大样本绝对值（线性峰值，0..1）。
    /// </summary>
    /// <param name="buffer">音频字节缓冲区。</param>
    /// <param name="bytesRecorded">有效字节数。</param>
    /// <param name="isIeeeFloat">是否为 IEEE 32 位浮点编码。</param>
    /// <param name="bitsPerSample">PCM 采样位深（16 或 32）。</param>
    /// <returns>线性峰值，范围 0..1。</returns>
    public static float ComputePeak(byte[] buffer, int bytesRecorded, bool isIeeeFloat, int bitsPerSample)
    {
        if (buffer is null || bytesRecorded <= 0) return 0f;

        float max = 0f;
        if (isIeeeFloat)
        {
            for (int n = 0; n + 4 <= bytesRecorded; n += 4)
            {
                float sample = Math.Abs(BitConverter.ToSingle(buffer, n));
                if (sample > max) max = sample;
            }
        }
        else if (bitsPerSample == 16)
        {
            for (int n = 0; n + 2 <= bytesRecorded; n += 2)
            {
                float sample = Math.Abs(BitConverter.ToInt16(buffer, n) / 32768f);
                if (sample > max) max = sample;
            }
        }
        else if (bitsPerSample == 32)
        {
            for (int n = 0; n + 4 <= bytesRecorded; n += 4)
            {
                float sample = Math.Abs(BitConverter.ToInt32(buffer, n) / (float)int.MaxValue);
                if (sample > max) max = sample;
            }
        }

        return Math.Clamp(max, 0f, 1f);
    }
}
