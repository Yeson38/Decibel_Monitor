using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using Decibel_Monitor.Models;

namespace Decibel_Monitor.Services;

/// <summary>
/// 插件全局设置服务：在插件配置目录中以 JSON 保存
/// <see cref="DecibelMonitorGlobalSettings"/>，并在修改后自动保存。
/// </summary>
public sealed class DecibelMonitorSettingsService : IDisposable
{
    private readonly object _saveLock = new();
    private readonly string? _filePath;
    private Timer? _saveTimer;
    private bool _disposed;

    /// <summary>
    /// 当前全局设置（属性变化自动触发延迟保存）。
    /// </summary>
    public DecibelMonitorGlobalSettings Settings { get; }

    /// <param name="configFolder">插件配置目录（<c>PluginBase.PluginConfigFolder</c>）；为空时仅内存保存。</param>
    public DecibelMonitorSettingsService(string? configFolder)
    {
        if (!string.IsNullOrWhiteSpace(configFolder))
        {
            _filePath = Path.Combine(configFolder, "plugin-settings.json");
        }

        // 整体反序列化得到完整实例：新增设置项无需在此处同步维护，避免漏拷贝
        Settings = LoadSettings() ?? new DecibelMonitorGlobalSettings();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;

        // 去抖后写盘，避免滑块拖动等高频变更频繁落盘
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ => Save(), null, 400, Timeout.Infinite);
    }

    /// <summary>
    /// 从磁盘读取全局设置；文件不存在或解析失败时返回 null（调用方回退到默认值）。
    /// </summary>
    private DecibelMonitorGlobalSettings? LoadSettings()
    {
        if (_filePath is null || !File.Exists(_filePath)) return null;
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DecibelMonitorGlobalSettings>(json);
        }
        catch
        {
            // 读取失败按默认值继续
            return null;
        }
    }

    /// <summary>
    /// 立即将全局设置写入磁盘。
    /// </summary>
    public void Save()
    {
        if (_filePath is null || _disposed) return;
        try
        {
            lock (_saveLock)
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings));
            }
        }
        catch
        {
            // 写盘失败不应影响运行
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _saveTimer?.Dispose();
        Save();
    }
}
