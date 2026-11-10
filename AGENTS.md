# AirBridge 开发规则

## 范围与基线

- 本目录是唯一维护的源码根目录，远程仓库为 `https://github.com/xxxxkq/AirBridge`，稳定分支为 `main`。
- 当前发布基线为 **3.0.65**。日常小修不改版本号、不新建 `AirBridge-版本` 目录；通过 Git 提交记录演进。阶段性交付才升版本。
- 工作流程见 [开发与发布](docs/DEVELOPMENT.md)，当前状态见 [项目进度](docs/PROGRESS.md)，设计依据见 [决策记录](docs/DECISIONS.md)。修改重要行为时同步更新对应文档。

## 实现约束

- 保持 C# + WPF 桌面端、原生 C++ 采集模块；运行时不得调用 PowerShell。构建和测试脚本可以使用 PowerShell。
- 使用现有 .NET Framework `csc.exe` 编译，保持现有 C# 语法兼容性（C# 5）；不要未经迁移就加入现代 .NET SDK 或语言特性。
- 保留现有 UI、托盘、设备模板、节点图、语言与设置兼容性。后台设备操作不得同步阻塞 WPF UI 线程。
- 手机通道按设备身份独立持有和释放；串行化连接控制不等于只允许单手机连接。设备页、托盘、节点图应共享连接状态和所有权，避免重复打开采集接口。
- 暂停零帧属于正常等待，不能仅凭零帧自动重连。不要把静音、供给不足、程序补零和主动丢弃混为一个指标。
- 音频关键路径不得同步写磁盘；队列必须有界。修改采集/渲染时保留欠载、丢弃、到达间隔与队列诊断，计时不得依赖日志开关。
- 不以加大缓冲代替定位问题；更改默认音频参数需说明延迟与流畅性的取舍。
- 不开发或安装自研/AAP驱动，不关闭 Secure Boot、不改启动设置。`Optional/` 为停止开发的历史研究，不进入默认构建或发布。已安装的 VB-CABLE 是可选扩展，不能成为普通用户的必需依赖。
- 正常退出、停止转发、异常恢复均须保留系统和应用原输出的恢复机制。任何新实机切换、蓝牙重置、安装或系统设置操作，应先说明影响并取得用户授权。
- 不伪造电量或信号。无数据用 `—`；缓存应说明时间和过期状态。佩戴检测仅对已适配的广播型号提供，不能承诺所有蓝牙耳机通用。
- GitHub自动检查默认关闭，只有启用后才进行自动网络检查；手动检查不能绕过“禁止更新”。更新安装必须由用户明确点击，等待音频进程和输出恢复，不通过杀用户进程强行更新。策略与位置见 `docs/UPDATING.md`，修改时运行 `Scripts/Test-Updater.ps1`。
- 数据根目录可由用户配置，不能再假定全部位于AppData。统一用Shared/DataLocation及本次运行的settings.Root；位置变化延后到恢复完成后的下次启动，不删除原数据、不覆盖非空目标。涉及位置解析/迁移时运行`Scripts/Test-DataLocation.ps1`，见`docs/STORAGE.md`。

## 构建

在 Windows x64、已安装 Visual Studio C++ x64 工具链、Windows SDK 和现有 .NET Framework 环境下，从仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputDirectory .\build\dev
```

使用显式输出目录，避免默认构建在仓库上级不断产生版本文件夹。依赖说明、实际工具发现方式见 `docs/DEVELOPMENT.md`。不在正在运行的发布目录中编译。

开发构建默认 `-Configuration Development`，定义 `AIRBRIDGE_DEVELOPMENT`。正式版用 `-Configuration Release -SingleFile`，默认运行组件目录 `build/release-runtime`，单文件分发位于 `artifacts/single-file/AirBridge.exe`。正式版必须从编译产物移除自检、实验对照与模拟故障，保留手机采集、恢复守护和必要诊断。禁止拿开发包直接改名为正式包。完整说明见 `docs/BUILD-VARIANTS.md`。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Test-Offline.ps1 -Release .\build\dev
```

该入口编译并执行缺音率、缓冲/抖动、参数与流水线测试，不打开真实音频设备。按改动范围选用其他 `Tests/` 测试，先读源码确认是否涉及设备、网络或系统设置。

UI 变更在交互式 Windows 桌面中另运行隔离自检：

```powershell
$p = Start-Process .\build\dev\AirBridge.exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw 'WPF self-test failed; inspect build/dev/self-test-*.txt' }
Get-Content .\build\dev\self-test-result.txt
```

`--self-test` 使用发布目录下独立的 `self-test-data`，不启动正常音频链路。离线通过不能宣称实机音质问题已经解决；记录实测设备、连接拓扑、缓冲开关及结果。未实测应明确标注。

## 提交与发布

- 开始前读 `git status` 和相关实现，保护未提交修改。功能分支完成一个目的明确的提交，提交说明写原因、行为和验证结果。
- 不提交 build/artifacts、旧发布包、本机诊断/设备身份、录音、配置、快捷方式、私钥或签名证书。NAudio 两个已纳管 DLL 是构建依赖例外，保留许可证。
- 本地构建输出留在 `build/`；可分享包放 `artifacts/`，上传 GitHub Release，不放入源码提交。
- 发布时保持 `Build.ps1` 默认版本、`Desktop/AssemblyInfo.cs`、`README.txt` 一致；创建 `v版本` 标签前核对实际二进制版本与干净的提交。
- 不自动发布、推送标签或操作仓库保护设置；这些动作按用户本次授权范围执行。不要因写文档而主动重启正在运行的 AirBridge。
