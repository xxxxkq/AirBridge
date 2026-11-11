# README 界面预览

这两张 PNG 由当前源码的 WPF 控件离屏渲染，使用合成的设备名称、标识、电量和连线。不包含真实用户配置、桌面、蓝牙地址或录音；不代表已连接硬件或证明音质/兼容性。

- `audio-graph.png`：音频管理与效果链演示，连线未应用。
- `devices.png`：音频设备卡片演示，数值不是实测值。

先生成开发构建，再执行：

```powershell
.\Build.ps1 -Configuration Development
.\Scripts\Export-ReadmeScreenshots.ps1
```

脚本仅运行隔离的文档渲染器，不显示窗口、模拟点击、打开音频流或写入用户真实配置。渲染器源码位于 `Tests/ReadmeScreenshots.cs`；截图保存到本目录，渲染缓存留在被 Git 忽略的 `build/readme-render/`。正式版不包含渲染器。

图片展示的是 main 的界面。发行包的具体行为以对应 Release 标签为准。
