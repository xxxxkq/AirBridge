# 开发构建与正式单文件

版本沿用3.0.65；构建类型不依靠版本号区分。

## 命令

```powershell
.\Build.ps1 -Configuration Development
.\Build.ps1 -Configuration Release -SingleFile
.\Scripts\Test-Offline.ps1 -Release .\build\dev
.\Scripts\Test-Offline.ps1 -Release .\build\release-runtime
.\Scripts\Test-BuildVariants.ps1
```

开发目录为 `build/dev`，正式运行组件为 `build/release-runtime`，单文件为 `artifacts/single-file/AirBridge.exe`。上述PowerShell只用于构建和测试，正式程序运行不调用脚本。不指定类型时默认Development，避免开发时丢失测试能力。`-SingleFile`仅允许Release。

## 编译边界

| 内容 | Development | Release |
| --- | --- | --- |
| 界面自检、迁移测试夹具 | 保留 | 编译排除 |
| 自动输出对照、音频AB实验、实验缓冲入口 | 保留 | 编译排除 |
| 手机模拟PCM、故障退出、慢启动、模拟挂起 | 保留 | 编译排除，工作进程构造也拒绝模拟模式 |
| 日志、帧数、缺音、队列诊断 | 保留 | 保留 |
| 手机真实采集/普通接收、退出清理 | 保留 | 保留 |
| 原输出恢复日志与守护 | 保留 | 保留 |
| UI资源、设备功能、可选VB-CABLE、DSP与网络组件 | 保留 | 保留 |

正式运行组件仅接受正常启动、`--background`和内部手机工作进程/恢复守护参数；单文件外部入口仅接受正常启动及`--background`。不认识的参数退出码为2，不进入界面。`--self-test`等开发入口在正式版不可用。

更新功能接入后，正式运行组件另接受内部`--apply-update <事务ID>`，用于用户确认后的临时安装进程；不是测试入口，单文件外部入口仍不接受该参数。更新策略及数据位置见`UPDATING.md`。

## 单文件实际行为

用户只需复制一个 `AirBridge.exe`。它内嵌15个明确列出的运行文件，包括现有.NET程序集、原生采集DLL、XAML、图标、配置与第三方声明。打包按允许清单进行，即使运行目录曾产生测试文件或日志，也不会打入包内。

首次启动释放至 `%LOCALAPPDATA%/AirBridge/runtime/<内容哈希>/`，后续校验并复用。缓存内容不原地覆盖，损坏时生成新副本；并发启动通过互斥协调。缓存是运行组件，不是遗留进程。旧缓存目前不自动删除，避免删除仍被手机或恢复进程使用的文件。必要时完全退出所有AirBridge进程后可以清理runtime子目录，下次重新释放；不要删除上层配置、输出恢复XML或用户录音。

上述是兼容默认位置。通过偏好设置配置数据目录后，释放至所选根目录的`runtime/`，软件旁保存小型位置描述XML。软件旁Data模式可随整个文件夹便携移动；详见`STORAGE.md`。

正式版仍依赖Windows x64与.NET Framework 4.8，不包含驱动或.NET安装程序。它是单文件分发，不是所有代码均永远只在内存中运行。使用当前已装的VB-CABLE仍为可选功能。

外层EXE在同一进程的隔离AppDomain加载正式组件，不添加常驻启动器进程；手机采集与输出恢复继续使用缓存内的EXE独立运行。打包器设置仅在进程内传播的启动路径，使软件中启用开机自启时指向用户持有的外层EXE，而不是缓存位置；不会在构建/测试时改现有开机入口。移动单文件后需要重新启用开机自启以更新快捷方式。

## 验证与交付

`Test-BuildVariants.ps1`检查开发/正式类型隔离、正式版拒绝模拟模式、保留恢复方法、包内容清单、并发释放、缓存校验与损坏重建，以及释放后程序集在独立AppDomain加载并拒绝自检入口。测试使用仓库build内缓存，不启动真实界面、不切换系统输出。

两种构建都运行既有离线音频套件。首次实际界面、手机播放、托盘、自启和异常退出恢复仍需用户手动验证；结构与离线通过不能替代这些测试。正式发布时上传单文件EXE及其SHA256；不能把含测试文件的开发目录整体上传。保留当前运行版作回退，正式标签和GitHub Release按用户授权操作。
