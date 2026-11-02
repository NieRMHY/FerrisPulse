# TrafficMonitor 本机电量快照

FerrisPulse 可把一次完整设备轮询的结果导出，供任务栏插件读取，无额外 HID 查询、服务或监听端口。此功能默认关闭。

## 开启与关闭

在当前用户注册表 `HKCU\Software\RazerBatteryTray` 新建 DWORD `ExportBatteryStatus=1`，等待下一次正常轮询或重启 FerrisPulse。快照路径为 `%LOCALAPPDATA%\FerrisPulse\battery-status.json`。设为 0 即停止后续导出；正常退出会清除本会话快照。

插件扩展位于 [NieRMHY/TrafficMonitor-NieRMHY](https://github.com/NieRMHY/TrafficMonitor-NieRMHY) 的 `PluginDemo/DeviceBatteryPlugin`。插件 INI 中设置 `api.snapshot_file`，并通过 `device0`、`device1`、`device2` 的 `source=Razer/Dareu/Maono` 分别选择设备。多个同品牌设备应改用快照内的 `device_id` 精确选择。

## 数据与时效

- 根对象包含 `producerPid`、随机 `producerSession`，以及 `data.schemaVersion=1`、`generatedAtUtc`、`devices`。
- 设备包括脱敏 `id`、`name`、`renamedName`、`source`、`status`、`battery`、充电/休眠状态与 `batteryLastUpdatedUtc`。未知电量是 JSON null，0% 是有效值。
- 原始设备路径和设备标识不导出；ID 为设备标识 SHA256 的前 8 字节，不能用作认证凭据。
- 同目录临时文件加原子替换，消费方不会读到半帧。插件检查文件时效、生产进程存活和进程创建时间，拒绝退出后遗留或 PID 重用的快照。
- 导出频率沿用 FerrisPulse 电量刷新设置；插件的文件刷新间隔不会增加无线硬件查询。插件默认 900 秒失效，长于此值的硬件刷新周期需要相应调整。
- 快照保留休眠/离线历史值及其状态，但 TrafficMonitor 插件对此显示 `--`，仅在线有效电量显示百分比；`77%+` 表示确认充电。

## 验证

`tests/test_battery_snapshot_export.ps1` 验证 Unicode/转义、0%/未知值、缓存状态、标识脱敏、时间戳与原子替换。与接收器回放测试及全量测试一同运行。读取快照只使用 .NET 4.0 系统程序集，不引入外部依赖。
