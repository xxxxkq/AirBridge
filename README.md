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

输出位于 `build/release`，不覆盖已安装的应用。默认不指定输出目录时，原构建脚本输出到上级目录的 `AirBridge-<版本>`。

## 目录

- `Core`、`Platform`：设备模型、音频路由与 Windows 平台接口。
- `Desktop`、`UI`、`Assets`：桌面程序、界面及资源。
- `Tests`：现有测试源码和运行脚本；部分测试需要实机设备或指定发布目录。
- `Optional`：实验性扩展，状态见 `Optional/DEVELOPMENT-STOPPED.txt`，不属于默认构建。
- `README.txt`：已有版本变更记录与使用说明。

## 维护方式

以 `main` 保存稳定基线；修改前创建功能分支，验证后提交并推送。
发布版本使用 `v<版本号>` 标签。更新版本时保持 `Build.ps1`、桌面版本声明和变更记录一致。
本仓库不包含旧版本安装目录、构建缓存、测试录音、本机迁移记录或签名私钥。
设备连接、音频质量等行为仍需实际 Windows 设备验证。
