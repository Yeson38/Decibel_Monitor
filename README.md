# Decibel_Monitor

一个用于 ClassIsland 的分贝监测插件，实时读取系统麦克风峰值并在主界面显示分贝映射值。支持校准（将数字音频幅度映射到目标 dBFS），并提供多重回退的采样方式以提高兼容性（AudioMeterInformation、WasapiCapture、WaveInEvent）。

## 主要功能

- 实时显示麦克风分贝值（映射到 0–150 范围以便在 UI 中直观显示）。
- 校准功能：根据目标 dBFS 自动计算并保存放大倍数（Settings.Magnification）。
- 多重回退采样：当设备不提供实时计量时，自动使用短时录音回退采样以获取峰值。
- 超出指定值自动显示提示文字（未实现）
- 超出指定值提醒（未实现）

## 依赖

- .NET 8
- ClassIsland

确保项目正确加载插件。

## 使用说明

1. 在宿主中将本组件加入布局（或通过插件管理启用）。
2. 主界面会显示当前分贝映射值（0–150）。
3. 打开组件设置面板进行校准：
   - 将麦克风对准参考声源或播放测试音。
   - 将参考滑块设置为目标 dBFS（默认 -80，范围 -150..0）。
   - 点击“校准”按钮。插件采样后计算放大倍数并保存到 `Settings.Magnification`（若设置控件未绑定则会提示并显示建议倍数）。

### 校准公式

- 测量得到的线性峰值：`measuredLinear`（0..1）
- 目标线性值：`targetLinear = 10^(targetDbFS / 20)`
- 放大倍数：`magnification = targetLinear / measuredLinear`

插件会对计算结果做上限保护（默认上限 1024；下限可为 0），以避免极端错误值。

## 设置项（DecibelComponentSettings）

- `Magnification` (double)：放大倍数，由校准或手动设置，默认 1.0。
- 参考 dBFS 在设置面板中通过滑块调整。

## 故障排查

- 分贝值长期显示 `0.0`：
  - 确认宿主应用有麦克风权限（Windows 隐私设置）。
  - 确认默认捕获设备已启用并未被独占。
  - 在设置面板使用“捕获设备列表”检查系统设备；增加音量或靠近声源再测。
- 校准结果异常：
  - 确认目标单位为 dBFS（数字音频相对量），而非声压级 dB SPL。
  - 若测量值接近 0，会导致放大倍数非常大，请提高测试音量或选择更高的目标 dBFS。

## 隐私与权限

- 插件需要访问麦克风进行本地采样，宿主必须授予麦克风权限。
- 插件不会上传或外传音频数据，所有采样仅在本地处理。

## 开发与贡献

- 欢迎提交 issue 或 PR。请遵循仓库中的贡献指南（若存在 CONTRIBUTING.md）提交风格一致的修改。
- Idea来自：[HAHAHAHAHAYINING](https://github.com/HAHAHAHAHAYINING),[讨论#561](https://github.com/ClassIsland/ClassIsland/discussions/561)
- 主要开发者：[Yeson38](https://github.com/Yeson38)
- 参考代码：[CIImage](https://github.com/lrsgzs/CIImage)
