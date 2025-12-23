using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models.ComponentSettings;

public partial class DecibelComponentSettings : ObservableObject
{
    // 放大倍数，校准后保存到此属性
    [ObservableProperty] private double _magnification = 1.0;
}
