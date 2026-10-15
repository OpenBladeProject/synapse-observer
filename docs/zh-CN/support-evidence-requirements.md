# OpenBlade 支持所需证据

[English](../support-evidence-requirements.md) | 简体中文 | [Русский](../ru-RU/support-evidence-requirements.md)

Synapse Observer 的采集流程根据 OpenBlade 安全支持一款笔记本所需的证据设计。观测器仅被动收集证据并生成草稿，不会据此将任何机型或命令纳入正式支持。

<a id="capability-families"></a>
## 能力类别

覆盖草稿保留 OpenBlade 设备覆盖模板中的每个最细分项目，并补充旧模板尚未表达的当前产品要求。完整类别如下：

- 身份与固件：USB 和 HID 描述符、机型编号、BIOS、EC、MCU、相关接口、HID 拓扑、Windows 平台详情、已签名驱动和软件的来源信息；
- 性能：预设、Custom CPU 和 GPU 级别、CPU 功率限制、CPU 电压优化、GPU 核心和显存偏移、使用及不使用散热底座时的 HyperBoost、HyperBoost 基础模式，以及足功率交流电、每个 USB-C 功率类别和电池下的回读；Windows 无法区分适配器类别时，还包括通用外部电源证据；
- 风扇：Automatic、固定范围和步长、最大值、手动曲线写入和回读，以及 RPM 查询；
- 传感器：CPU/GPU 温度、风扇 RPM 和传感器健康状态报告；
- 电池：保护关闭、公布的上限、上限查询、临时充满及其查询；
- 键盘灯效：Off、亮度、getter、矩阵与 LampArray 发现、Windows 主机渲染、布局身份、物理位置映射，以及每种效果的颜色、持续时间、方向、区域、键位映射、帧格式和频率；
- 徽标灯效：Off、Static、Breathing 及 getter；
- 键盘行为：功能键优先级、Gaming mode、启动动画、Normal 和 Driver 设备模式、播放静音指示及其 getter；
- 特殊按键：Fn 层报告、性能键、M1 至 M5，以及不预设这些产品角色的通用提示式输入证据；
- 显示：Windows 可见模式、动态和可变刷新率、HDR、SDR/HDR 色彩配置文件、面板亮度及机型专用的原生亮度驱动路径；
- 生命周期：冷启动、登录、服务重启、睡眠与恢复、设备重连；
- 电源：足功率交流电、USB-C 类别、适配器功率查询、电池及它们之间的每种转换；
- 冲突：厂商控制器存在、外部更改、默认零写入策略及显式重新接管；
- 失败：超时、格式错误响应、校验和/事务/命令/状态不匹配、不支持的 PID 或固件，以及事务中断开连接。

采集器不限于 OpenBlade 当前生产支持机型。它保留 Blade Pro、Stealth、14、15、16、17、18 以及不熟悉 Blade 产品名称和 PID 的发现证据。发现过程绝不从另一机型复制接口、报告结构或命令。

<a id="what-passive-collection-can-establish"></a>
## 被动采集能够证明什么

`coverage-draft.json` 仅使用 `NotInvestigated` 和 `Captured`。`Captured` 表示 ZIP 包含该项目的真实观测，且观测范围有限。Windows 清单可为身份、描述符、显示、驱动或 LampArray 项目提供证据。
Feature 项目需要通过隐私审核的报告，而且报告必须位于完整的操作时间窗口内，窗口中包含初始、更改、操作和恢复标记。特殊按键项目需要按提示操作得到的输入，以及相匹配的按键、模式和探测摘要。如果只有提示信息，就只能更新通用输入证据项目。要确认按键的具体功能，还需独立观测到的 Windows 按键或经过审核的信号。

这一状态只确认上述范围。采集到某个亮度、颜色、方向、矩阵行或性能级别，不能证明整个范围都受支持。识别报告结构是隐私判断，不是命令定义。用户标记描述贡献者选择的内容，不证明固件回读或恢复成功。不支持的报告正文仍被隐去，保留结构和质量缺口。

观测层为 `UserModeFeatureApi`、`WindowsRawInput` 和 `WindowsInventory`，不得改称 USB 传输。导出的草稿不是 PCAP 文件，也不声称符合独立 OpenBlade 抓包仓库的注释验证器要求。

<a id="what-maintainers-must-still-do"></a>
## 维护者仍需完成的工作

生产支持需要独立且经过审核的流程：

1. 为精确的机型、PID、固件和接口提交经过清理的参考程序抓包。
2. 确定性地解码请求与响应，包括长度、校验和、事务、状态、错误及超时处理。
3. 实现无需厂商进程的类型化只读查询。
4. 保存已确认的原状态，应用一个类型化值，通过独立回读确认，恢复精确的保存状态，再通过回读确认恢复。
5. 证明不支持的设备和固件规则会产生零写入。
6. 验证控制器冲突、相关电源上下文、重启和重连行为，并在需要时进行有人值守的睡眠/恢复验证。
7. 让生产代码使用已提交且经过清理的测试数据进行验证，并完成明确的机型准入审核。

提交或回读有歧义时，停止验证并转为只读。维护者不得重放结果不明的写入，也不得以用户报告的恢复代替确认。

源检查表使用格式版本 2，即 [`device-coverage.template.json`](https://github.com/OpenBladeProject/openblade-captures/blob/main/templates/device-coverage.template.json)，各阶段所需的完整证据见抓包仓库的 [`CAPTURE_GUIDE.md`](https://github.com/OpenBladeProject/openblade-captures/blob/main/CAPTURE_GUIDE.md)。

数值范围不是本采集器的生产准入规则。在经过精确审核的散热、Custom CPU/GPU、固定风扇和充电上限命令族内，不熟悉的数值会保留，并标记 `numericMeaningUninterpreted: true`。报告族、控制标签、通道、长度、校验和、填充、来源和状态检查仍然适用。查询输入参数必须符合已审核的全零结构。这样可以保留新机型的数值供研究，但不代表已理解其含义或已支持这些值。
