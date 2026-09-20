using System;
using System.Linq;
using NAudio.CoreAudioApi;

namespace Decibel_Monitor.Measurement;

/// <summary>
/// 捕获设备枚举与缓存（默认捕获设备 / 设备描述列表，枚举结果带 5 秒缓存）。
/// 由 <see cref="AudioPeakMeter"/> 拆分而来，对外行为保持一致。
/// </summary>
internal sealed class AudioDeviceEnumerator : IDisposable
{
    /// <summary>设备列表缓存刷新间隔（毫秒）。</summary>
    private const int DeviceRefreshIntervalMs = 5000;

    private readonly object _cacheLock = new();
    private readonly MMDeviceEnumerator _enumerator = new();

    private MMDevice[]? _cachedCaptureDevices;
    private MMDevice? _cachedDefaultDevice;
    private DateTime _deviceCacheTimeUtc = DateTime.MinValue;

    /// <summary>
    /// 获取默认捕获设备（设备列表带 5 秒缓存）。
    /// </summary>
    public MMDevice? GetDefaultCaptureDevice()
    {
        try
        {
            EnsureCache();
            lock (_cacheLock)
            {
                if (_cachedDefaultDevice is null) return null;
                if (_cachedCaptureDevices is null) return _cachedDefaultDevice;
                return _cachedCaptureDevices.FirstOrDefault(c => c.ID == _cachedDefaultDevice.ID) ?? _cachedDefaultDevice;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判断指定设备是否为默认捕获设备（null 表示默认设备）。
    /// </summary>
    public bool IsDefaultDevice(MMDevice? device)
    {
        if (device is null) return true;
        var def = _cachedDefaultDevice;
        return def is null || def.ID == device.ID;
    }

    /// <summary>
    /// 获取当前可用捕获设备的描述列表（用于诊断显示）。
    /// </summary>
    public string[] GetCaptureDeviceDescriptions()
    {
        try
        {
            EnsureCache();
            lock (_cacheLock)
            {
                return _cachedCaptureDevices?.Select(d => $"{d.FriendlyName} ({d.ID})").ToArray() ?? Array.Empty<string>();
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 获取默认捕获设备的描述（用于诊断显示）。
    /// </summary>
    public string? GetDefaultCaptureDeviceDescription()
    {
        try
        {
            var d = GetDefaultCaptureDevice();
            return d is null ? null : $"{d.FriendlyName} ({d.ID})";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 刷新设备缓存（超过缓存时间或尚未初始化时重新枚举）。
    /// </summary>
    private void EnsureCache()
    {
        lock (_cacheLock)
        {
            var now = DateTime.UtcNow;
            if (_cachedCaptureDevices is null || (now - _deviceCacheTimeUtc).TotalMilliseconds >= DeviceRefreshIntervalMs)
            {
                _cachedCaptureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                _cachedDefaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                _deviceCacheTimeUtc = now;
            }
        }
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
