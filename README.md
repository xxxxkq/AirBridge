# AirBridge

Windows 桌面音频路由应用，使用 C# / WPF 和原生 C++ 音频采集模块。
本仓库从当前 **3.0.65** 源码建立维护基线。

## 构建

需要 Windows x64、.NET Framework 4.x、Visual Studio C++ x64 Build Tools 和 Windows SDK。
仓库保留构建所需的 NAudio 2.2.1 DLL，许可证见 `THIRD-PARTY-NOTICES.txt`。

在仓库目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputDirectory .\build\release
```

输出位于 `build/release`，不覆盖已安装的应用。默认不指定输出目录时输出到仓库内 `build/dev`。

## 目录

- `Core`、`Platform`：设备模型、音频路由与 Windows 平台接口。
- `Desktop`、`UI`、`Assets`：桌面程序、界面及资源。
- `Tests`：现有测试源码和运行脚本；部分测试需要实机设备或指定发布目录。
- `Optional`：实验性扩展，状态见 `Optional/DEVELOPMENT-STOPPED.txt`，不属于默认构建。
- `README.txt`：已有版本变更记录与使用说明。

## 维护方式

开发/正式构建已分开：`Build.ps1 -Configuration Development` 保留开发测试；`Build.ps1 -Configuration Release -SingleFile` 生成正式单文件 `artifacts/single-file/AirBridge.exe`。正式版编译排除自检、故障模拟和实验入口，保留日志与输出恢复。单文件会向用户缓存释放必要组件，详见 [构建类型与单文件说明](docs/BUILD-VARIANTS.md)。

- [项目规则与验证入口](AGENTS.md)
- [开发、提交与发布流程](docs/DEVELOPMENT.md)
- [项目进度、已知问题与下一步](docs/PROGRESS.md)
- [重要设计决策及理由](docs/DECISIONS.md)

日常修改沿用当前版本，构建到 `build/dev`，不再每次新建版本目录；阶段性发布才统一升版本。离线检查入口：`Scripts/Test-Offline.ps1 -Release .\build\dev`。

以 `main` 保存稳定基线；修改前创建功能分支，验证后提交并推送。
发布版本使用 `v<版本号>` 标签。更新版本时保持 `Build.ps1`、桌面版本声明和变更记录一致。
本仓库不包含旧版本安装目录、构建缓存、测试录音、本机迁移记录或签名私钥。
设备连接、音频质量等行为仍需实际 Windows 设备验证。

## 开源协议

AirBridge 自有源码采用 **GNU GPL-3.0-only**，完整协议见 [LICENSE](LICENSE)。分发修改版时须遵守 GPL-3.0 的源码提供等条件。第三方依赖及上游研究文件保留各自许可和版权，见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

`Optional/` 是已停止的驱动研究，不属于正式构建或二进制分发；公开源码不代表建议安装实验驱动。AirBridge 并非 Apple 官方软件。正式二进制仅通过仓库 Releases 提供；没有发布附件时请按文档自行构建。
