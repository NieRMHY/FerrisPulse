# FerrisPulse：2.4G 外设电量与硬件状态

**主开发目录**：`/home/niermhy/workspace/project/project_myself/github_project/FerrisPulse`

**项目文档根**：`/home/niermhy/workspace/Obsidian/Obsidian_Note_Server/工作知识记录/个人开发项目/FerrisPulse/`

**独立 GitHub 仓库**：`https://github.com/NieRMHY/FerrisPulse.git`

**接力开发及 GitHub 默认分支**：`integration/trafficmonitor-battery`

<!-- Add by MHY, 2026-11-17 配置 miniPC 主开发工作区与多端同步规范 -->

## 项目概述

FerrisPulse（灵脉）通过 Windows HID 读取外设电量和状态，并向 TrafficMonitor 发布本机快照。个人分支包含达尔优 AE7、闪克 PD200W、雷蛇 Cobra Pro 和飞智八爪鱼 6 的适配及状态修正。各设备的可用控制能力以实际验证为准。

## 工作方式与规则适用

- 个人仓库只配置 `origin` 指向 `NieRMHY/FerrisPulse`，不配置上游 remote 或自动合并；原项目来源见 README，保留许可证和版权信息。
- miniPC（Linux）是主开发机；此仓库保留完整 Git 历史和远程关联。Windows 原生功能在 Windows 构建与验收。
- 开始任务先读本文件及适用的 `AGENTS.md`，按任务查阅模块文档；不要求每次通读所有协议材料。
- 源码沿用上游命名、目录和语言习惯；公共模板的通用命名规则不用于批量改名。修改标注使用所属语言合法的注释形式。
- 保留上游 README、许可证和技术文档；本机介绍、方案及工作记录放上述 vault 文档根。本次用户授权建立这些介绍和规则。
- 已授权范围内直接完成编辑、验证及所需 Git 操作；提交、推送和发布以当次用户授权为准，已有授权无需重复确认。2026-11-17 已授权将三个项目独立维护并同步这些规则。
- 多端同步以 Git 提交为单位：先检查分支和工作树，再 `git fetch origin`；有对应追踪分支且工作树干净时使用 `git pull --ff-only`。发现分叉或未提交修改时保留现场，禁止覆盖或强推。
- 当前 Windows 来源：`D:\FerrisPulse · 灵脉`。通过 Git 在两端交换后续代码；不要反复复制整个工作区或覆盖 `.git`。
- 网络沿用环境文档的会话代理。2026-11-17 初始化时既有代理不可用，临时经 Windows SSH 隧道取回 GitHub 数据；临时端口没有写入 Git 配置。后续先核对网络，不写入失效的临时地址。

## 环境与常用命令

- C# 5.0 / .NET Framework 4.0，WinForms + Win32 P/Invoke，仅系统程序集，不引入 NuGet 或第三方运行 DLL。
- Windows 的 `build.bat` 使用系统 `csc.exe`。源码 `.cs/.bat/.md/.ps1` 保持 UTF-8 BOM，编译保留 `/codepage:65001`。
- miniPC Linux 是主开发工作区；Windows 构建副本可位于该机实际仓库根目录，不再以某个历史 D 盘路径限制全部开发。
- 目前未在 miniPC 验证 Windows 编译和硬件运行；Linux 的 .NET/Mono 不能替代原生 Win32、HID 和桌面验收。

Windows 仓库根目录：

```powershell
cmd /c build.bat
python tools/ensure_boms_safe.py
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

按改动选择相关 `tests/test_*.ps1`，全量命令用于需要整体回归时。运行前检查测试是否写 HKCU 或访问 HID，使用隔离配置或回放；不要为构建源码关闭程序目录中的独立正式实例。

## 架构与核心规则

- `src/Core/`：入口、P/Invoke、配置与自启动；`src/Hardware/`：HID 驱动、注册表、命令总线与状态缓存。
- `src/Models/`：设备与能力契约；`src/Services/`：后台服务和快照；`src/UI/`：WinForms、主题和托盘。
- `tests/` 为回放及自动化验证，`tools/` 为构建维护脚本，`docs/` 保留随源码维护的架构和协议文档。
- 先遵循 `AGENTS.md`、`AGENT_RULES.md` 与 `docs/AGENT_HANDBOOK.md` 的工程约束；本文件补充 miniPC 的工作区和文档入口，旧的 Windows 单机路径不再是主开发目录。
- UI 只使用 `DeviceCapabilities`，硬件操作经 `HardwareCommandBus`；外部脚本、JSON 适配器和内置驱动按既有优先级处理。
- 按 VID/PID、UsagePage/Usage 及精确报告长度隔离接收器；保留键盘 0 权限打开、虚拟 HID 过滤和设备身份隔离。
- 未验证的写能力保持关闭；新设备依据真实报文和回放测试适配，不能轮流盲发不同品牌协议。
- 采集结果与心跳分开：`generatedAtUtc` 不等于 `observedAtUtc`；局部刷新不能推进其他设备的采样时间。未知值用 null，缓存值保持原观察时间。
- 反射测试使用字节读取后 `Assembly.Load`，不用 `Add-Type -Path` 锁住可执行文件。
- 保留任务栏重建、自启动恢复、DWM 弹窗处理和现有维护准则注释；发布规则仅在实际发布任务适用，不在普通开发时回退版本或部署运行实例。
- 不提交编译产物、原厂 DLL、抓包日志或私人运行配置。Windows 正式运行目录是 `D:\Tools\FerrisPulse_v2.2.0_Portable`。

## 按任务阅读

- 联动契约：`docs/TRAFFICMONITOR_INTEGRATION.md`。
- 设备适配：`docs/DEVICE_ADAPTATION_AE7_PD200W.md`、`docs/HARDWARE_PROTOCOLS.md` 的相关章节。
- 扩展包格式：`docs/ADAPTER_SPECIFICATION.md`；架构：`ARCHITECTURE.md`。
- 消费侧实现：`../TrafficMonitor-NieRMHY/PluginDemo/DeviceBatteryPlugin/`。

## 初始化验证与交付状态

- 源码基线：`519ef6bec9e39445501d843cd99d8e261f70f31e`（2026-11-17）。具体文件比对与环境限制见 vault `工作记录/2026-11-17_迁入miniPC与源码核验.md`。
- 本次将 README 来源说明和开发规则提交到独立个人仓库；未执行原生程序构建及硬件测试，不能把历史测试结果当作此次验证。

## 公共规则

以下按现有项目约定复制自 vault 根目录 `_project_CLAUDE_template.md`；已确定的文档根与平台边界以上方为准。

## 文档规则
- 项目过程中按需生成记录、说明文档，统一存放在 Obsidian vault
- **Vault 根路径**: `/home/niermhy/workspace/Obsidian/Obsidian_Note_Server/`
- 开始时先检索 vault 目录结构，找到与项目领域匹配的分类目录；无合适分类则在 vault 根目录新建
- 在该分类下创建本项目子目录作为文档根
- 确定文档路径后，将完整路径写入本文档顶部，后续直接使用无需重复检索
- 生成/更新/读取文档 → 均从 vault 对应路径操作
- 写入与生成文档时遵循 vault 根目录 `CLAUDE.md` 的整理规则
- 使用 `[[内部链接]]` 在文档之间交叉引用

### 工作记录
- 中间过程记录、阶段总结、plan 方案均写入本项目文档目录下的 `工作记录/` 子目录
- 记录文档命名：`YYYY-MM-DD_简短描述.md`
- Plan 文档命名：`_Plan_描述.md`
- 已完成的 plan 在正文开头标注完成日期

## 工作区环境汇总
- 阅读 `/home/niermhy/workspace/environment/README.md`（含服务器连接、SDK 共享、烧写等索引）
- environment 文档变更后运行 `env-sync` 同步 vault 镜像（`信息记录/environment镜像/`）

## 语言
- 沟通使用简体中文，代码标识符使用英文
- 日志信息：优先使用中文，或英文

## 网络环境
- 当前环境：国内网络，GitHub 直连不稳定
- 需要访问外部网络时先 `proxy-on`（`~/scripts/ConfigProxy.sh`，结束后可 `proxy-off`）

## 命名规范
- 函数/类名：PascalCase（大驼峰）
- 变量名：camelCase（小驼峰）
- 文件名：camelCase（小驼峰），少用连字符或下划线

## 代码风格
- 不写多余注释，只写"为什么"而非"是什么"
- 修改代码时添加标注：`# Add by MHY, <说明>` 或 `# Modify by MHY, <说明>`
- 不引入不必要的抽象，三行相似代码好过一次过早的封装
- 只处理真实发生的场景，不为"可能"做防御性编程
- 不写 docstring、不写 README（项目记录/Plan 写入 vault 工作记录目录，不在此列）

## 工具使用
- 优先使用 Read/Edit/Write 工具而非 cat/sed/awk
- 独立操作并行执行（多个 Bash 调用或 Agent 调用同时发出）
- git 操作需确认，不做 force push / reset --hard 等破坏性操作
- 提交时作者使用当前 git config，不添加 Co-Authored-By（除非明确要求）
