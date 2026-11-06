# TrafficMonitor 本机电量快照

FerrisPulse 将完整设备轮询结果保留为不可变快照，并每 3 秒独立发布心跳，供任务栏插件持续读取，无额外 HID 查询、服务或监听端口。此功能默认关闭。

## 开启与关闭

在实际运行 FerrisPulse 的 Windows 用户上下文中，设置 `HKCU\Software\FerrisPulse` 的 DWORD `ExportBatteryStatus=1`。通过统一 `ConfigManager` 读取；新键缺失时兼容 `HKCU\Software\RazerBatteryTray`。快照路径为 `%LOCALAPPDATA%\FerrisPulse\battery-status.json`。开关在下次心跳生效，不需重启；设为 0 或正常退出都会清除本会话快照。

不能只用调试终端的注册表读取结果确认配置成功：应正常从 Windows 桌面启动应用，核对它实际生成的快照以及 TrafficMonitor 宿主的读取结果。两边启动顺序独立，缺失的文件会在后续发布/读取周期自动恢复。

插件扩展位于 [NieRMHY/TrafficMonitor-NieRMHY](https://github.com/NieRMHY/TrafficMonitor-NieRMHY) 的 `PluginDemo/DeviceBatteryPlugin`。插件 INI 中设置 `api.snapshot_file`，并通过 `device0` 至 `device3` 的 `source=Razer/Dareu/Maono/Flydigi` 分别选择雷蛇、AE7、MIC 和手柄。四项方案使用 `display.item_count=4`、`trailing_spacer=0`、`align_columns=1`，固定标签与 0%–100% 数值列宽。多个同品牌设备应改用快照内的 `device_id` 精确选择。

## 数据与时效

- 根对象包含 `producerPid`、随机 `producerSession`，以及 `data.schemaVersion=1`、`generatedAtUtc`、`observedAtUtc`、`heartbeatIntervalMs`、`devices`。
- 设备包括脱敏 `id`、`name`、`renamedName`、`source`、`status`、`battery`、充电/休眠状态与 `batteryLastUpdatedUtc`。未知电量是 JSON null，0% 是有效值。
- 原始设备路径和设备标识不导出；ID 为设备标识 SHA256 的前 8 字节，不能用作认证凭据。
- 同目录临时文件加原子替换，消费方不会读到半帧。插件检查文件时效、生产进程存活和进程创建时间，拒绝退出后遗留或 PID 重用的快照。
- `generatedAtUtc` 随每 3 秒心跳更新；`observedAtUtc` 只在完整硬件查询成功后更新。硬件查询仍沿用用户电量刷新设置，心跳不会让旧观察值变新。
- 鼠标活动只触发匹配目标的电量查询和 `PublishPartial`，不推进整组 `observedAtUtc` 或另一台设备的采样时间；输入合并和冷却不会改变原定轮询频率。
- 插件每 3 秒重新打开快照，默认拒绝超过 15 秒没有心跳或超过 900 秒没有新硬件观察的数据。旧版无心跳字段的快照仍兼容原来的文件时效策略。
- 首次硬件查询完成前发送空设备列表；文件被删除或暂时无法替换时自动重试。退出先停定时器再清除自己会话的快照，防止退出后重新生成文件。
- 快照保留休眠/离线历史值及其状态，但 TrafficMonitor 插件对此显示 `--`，仅在线有效电量显示百分比；`77%+` 表示确认充电。

## 验证

`tests/test_battery_snapshot_export.ps1` 验证 Unicode/转义、0%/未知值、缓存状态、标识脱敏、时间戳与原子替换。`tests/test_battery_snapshot_heartbeat.ps1` 在临时目录验证独立心跳、不可变观察、运行时开关、文件重建和退出竞争；不写生产配置、不查询硬件。与接收器回放测试及全量测试一同运行，仅使用 .NET 4.0 系统程序集。
