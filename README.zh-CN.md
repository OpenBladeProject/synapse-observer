# Synapse Observer

[English](README.md) | 简体中文 | [Русский](README.ru-RU.md)

收集笔记本信息并记录 Synapse 中的设置变化，帮助 [OpenBlade](https://github.com/OpenBladeProject/openblade-core) 支持你的 Razer Blade。保存和提交结果无需使用终端，也无需下载仓库源码。

[下载最新版本](https://github.com/OpenBladeProject/synapse-observer/releases/latest)，解压 ZIP，然后正常打开 `SynapseObserver.exe`。

<a id="what-you-need"></a>
## 所需环境

- Windows 11，x64。
- [.NET 10 Desktop Runtime，x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。需要单独下载；如果尚未安装，Windows 会提供安装链接。
- 记录设置变化需要 Synapse；仅收集设备信息不需要它。

可以收集 Blade、Blade Pro 和 Blade Stealth 的信息。能否记录取决于 Synapse 与笔记本通信的方式。请参阅[机型覆盖范围](docs/zh-CN/model-coverage.md)。

<a id="send-evidence"></a>
## 提供证据

1. 打开应用，选择 **Collect device information**（收集设备信息）。
2. 如果还想记录设置变化，在 Synapse 中打开笔记本控制页面。在同一 Windows 账户下正常启动两个应用，选择 **Check recording availability**（检查是否可以记录），并授权观测本次会话。选择 **Start recording**（开始记录）。
3. 选择一项要采集的证据，输入 Synapse 显示的准确设置值。在更改前选择 **Begin this step**（开始此步骤）。
4. 在 Synapse 中更改一项设置。等待同步完成并显示新值后，输入新值并选择 **I changed it**（已更改）。恢复准确的初始设置，再次等待同步完成，确认控件显示该值，然后选择 **I restored it**（已恢复）。
5. 对于不熟悉的值，使用 **Other state…**（其他状态）。无法测试的项目请选择 **Skip**（跳过）或 **Setting unavailable**（设置不可用）。
6. 如果记录了设置，选择 **Stop recording**（停止记录），再选择 **Save evidence ZIP…**（保存证据 ZIP）。无法记录或跳过记录时，仅含设备信息的 ZIP 仍然有用。

引导流程涵盖性能、风扇、键盘灯效和布局、发光徽标、特殊按键、电池设置及显示控制。可选的 M 键/Fn 检查需要在采集器窗口处于前台时按下提示的按键。每次检查持续五秒。单独按 Fn 可能不会产生此检查能够观测到的输入。你仍可在 Fn key 证据项中报告它的可见行为。

请参阅 [ZIP 的内容](docs/zh-CN/capture-contents.md)。

<a id="submit-the-zip"></a>
### 提交 ZIP

1. 上传到 GitHub 前先检查 ZIP。支持请求及其附件是公开的。采集器不会上传任何内容。
2. 在现有[设备支持请求](https://github.com/OpenBladeProject/openblade-core/issues?q=is%3Aissue%20label%3Adevice-support)中搜索你的机型编号。将 ZIP 附到对应请求，或打开[新设备支持表单](https://github.com/OpenBladeProject/openblade-core/issues/new?template=device-support.yml)。
3. 提供笔记本名称、RZ09 机型编号、BIOS 和 Synapse 版本、已安装的 OpenBlade 版本，以及测试过、跳过或无法记录的设置。
4. 在新请求表单的 **Evidence ZIP**（证据 ZIP）处添加 Synapse Observer ZIP。对于已有请求，在评论中附上 ZIP。如果 OpenBlade 的 **Help add support**（协助添加支持）按钮还保存了 `OpenBlade-identity-report.zip`，也请一并附上。
5. 等待每个 ZIP 上传完毕，再提交表单或发布评论。

<a id="recording-notes"></a>
## 记录说明

你在 Synapse 中更改和恢复设置。采集器观测其活动，并记录你的操作标记。每次记录最长十分钟。停止后，可以保持此窗口打开并继续记录更多设置。如果无法确认清理完成，应用会提示你在再次记录前重新启动 Synapse。

录制期间，应用会将隐私过滤后的报告保存到本地。繁忙的灯效不会耗尽固定的报告配额。完成设置检查后停止录制，再保存一个证据 ZIP。如果本地存储失败，请恢复尚未完成的设置，并将部分证据保存到其他位置。

等待 **Syncing Profiles** 结束后，再更改或恢复另一项设置。如果 Synapse 一直卡住，请停止记录并保存部分证据 ZIP。Synapse 恢复响应后，再恢复尚未恢复的设置。部分证据 ZIP 仍然有用。

不会自动上传任何内容。分享前请检查 ZIP。普通键入、宏、鼠标输入、设备路径和清单中的序列号字段都会被排除。未识别或可能包含身份信息的命令内容不会导出；经过审核的数值报告和灯效报告可以保留原始字节。

这是未签名的开发版本。实机试验已在一台 Blade 16 上记录性能变化；其他笔记本和 Synapse 后端仍需测试。请保持安全防护开启。维护者会另行审查证据，测试能否支持该机型；一次记录不能确认设置已到达笔记本固件。

<a id="further-details"></a>
## 更多信息

阅读[证据格式](docs/zh-CN/evidence-format.md)、[支持所需证据](docs/zh-CN/support-evidence-requirements.md)或[验证状态](docs/zh-CN/VALIDATION.md)。开发相关内容见[构建与验证](docs/building.md)及[原生观测器说明](native/README.md)。

采用 [GPL-3.0-or-later](LICENSE) 许可证。依赖项声明包含在 ZIP 中。
