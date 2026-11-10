# 开发、提交与发布

## 目录边界

仓库：`https://github.com/xxxxkq/AirBridge`。源码根目录：本仓库的本地检出目录。在该目录执行 Git 与构建命令；桌面的其他研究项目、旧源码和运行包不属于此仓库。

| 目录/文件 | 用途 | 纳入Git |
| --- | --- | --- |
| Core / Platform / Desktop / UI / Assets | 模型、平台与音频、桌面、界面资源 | 是 |
| Dependencies | 已固定的NAudio 2.2.1 DLL，保留第三方声明 | 是，仅已明确纳管依赖 |
| Tests / Scripts | 测试源码和维护入口 | 是 |
| docs / AGENTS.md / README.md | 流程、状态、设计规则 | 是 |
| README.txt / MIGRATION.txt | 历史变更与迁移映射 | 是，历史记录不必重写 |
| Optional | 停止开发的驱动/AAP研究 | 是，默认构建与分发排除 |
| build | 编译缓存、dev、test、诊断、本机迁移存档 | 否 |
| artifacts | 待分享压缩包、校验和 | 否 |
| 工作区02_AirBridge运行版本目录 | 本机正在运行/少量回退包 | 否 |

不要把旧运行包当作源码修改。已有 build 缓存和第三方研究下载暂不整体搬动，避免破坏历史脚本的本机引用；Git已排除。禁止把签名私钥、录音、个人配置或诊断设备身份上传到仓库。

## 开始一项修改

```powershell
git status --short
git switch main
git pull --ff-only
git switch -c fix/short-description
```

有未提交修改时先确认归属，不覆盖、不强制reset。功能用`feat/...`，文档用`docs/...`。先读 `AGENTS.md` 和相关实现，完成后更新进度/决策。日常沿用当前版本，不为了提交新建发布目录。

## 本地构建和验证

需要 Windows x64；Visual Studio Build Tools 的“使用C++的桌面开发”（MSVC x64/x86、Windows SDK）；现有 .NET Framework编译器、WPF与系统WinMetadata。`BuildNative.ps1`通过vswhere发现工具链，C++17静态CRT编译采集DLL；`Build.ps1`用Framework64的csc编译托管程序。干净机器若缺依赖应补齐构建工具，不要安装驱动或改系统输出来尝试修复编译。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputDirectory .\build\dev
powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Test-Offline.ps1 -Release .\build\dev
```

离线测试使用 `build/tests/offline` 的独立程序集副本。运行日志可由调用者保存到 `build/`，不要提交机器日志。

UI修改仅在Development构建使用`AGENTS.md`里的`--self-test`，Release已排除该入口。该检查需要交互式Windows桌面；日志和截图在测试输出目录。开发/正式隔离和单文件检查使用`Scripts/Test-BuildVariants.ps1`，细节见`BUILD-VARIANTS.md`。网络、真实音频、输出恢复、手机连接测试在授权后按单次清晰计划进行，记录开始状态并在结束恢复。仅改文档无需进行真实音频测试。界面启动与操作默认由用户手动完成。

## 提交与合并

```powershell
git diff --check
git diff --stat
# 用明确路径添加本次修改，不盲目 git add .
git add AGENTS.md docs Scripts
git diff --cached
git commit -m "docs: document development and release workflow"
git push -u origin HEAD
```

最后两步在需要交付提交/远端时执行。通过GitHub PR合入main，描述问题、最终行为、验证结果及未实测限制；小修可以一个提交完成。合并方式保持一致，建议Squash，保留可定位的提交。仓库保护、CI和自动发布目前没有在本次配置，未来可逐步增加；不要把建议当作已启用功能。

## 阶段性发布

1. 从已验证的main提交准备发布，工作区干净。确定新版本；同步修改`Build.ps1`默认版本、`Desktop/AssemblyInfo.cs`两项版本及`README.txt`说明。
2. 在`build/release-candidate`干净目录构建，执行离线检查和隔离WPF检查。不要覆盖当前运行包。仅传`-Version`不会自动更新AssemblyInfo，必须核对两处一致。
3. 记录实际硬件验证；对未验证机型/功能写明限制。提交发布变更，记录提交SHA。保留最后一个已验证回退包。
4. 从同一提交用`-Configuration Release -SingleFile`重新构建，确认二进制版本，运行构建隔离检查。正式版不再运行`--self-test`，不能把Development产物当作正式版。
5. 正式交付`artifacts/single-file/AirBridge.exe`及SHA256。该EXE内嵌允许清单中的DLL、UI、Assets、config与第三方声明，首次运行自动释放。只分享这个单文件EXE即可；不要混淆多文件运行组件目录里的同名EXE。打包器不会包含测试数据、用户配置或驱动。
6. 在明确发布授权下推送`v版本`标签，创建GitHub Release，上传单文件及校验和，说明要求、变化、验证与回退。标签指向实际构建提交；不重用标签覆盖已公开的版本。

示例（版本为占位符，执行前替换）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Version X.Y.Z -Configuration Release -SingleFile
Get-FileHash .\artifacts\single-file\AirBridge.exe -Algorithm SHA256
git tag -a vX.Y.Z -m "AirBridge X.Y.Z"
git push origin vX.Y.Z
```

实际执行时每条命令成功后再继续。包不是安装程序，不自动安装VB-CABLE，也未承诺Authenticode签名。若标签/发布失败，先检查远端状态，避免重复或覆盖。

## 本地整理与回退

日常固定`build/dev`，测试副本在`build/tests`，正式包在`artifacts`。停止运行的dev实例后再重建。运行包已迁至工作区 `02_AirBridge运行版本`，源码位于 `01_项目源码/AirBridge.Managed`；开机启动入口已同步更新。

根目录原有的本机迁移快捷方式、`startup-migration.json` 和 `ROLLBACK-2.49.7.json` 已归档至 `build/local-history/`，仅用于本机追溯，不进入Git。当前运行包和AppData设置未改动。

源码回退优先用Git：未发布分支可切换，已合并错误用`git revert`生成修复提交。运行包回退先退出新包，再启动保留包，保留用户设置备份并核对新旧兼容。不要用git clean删除尚未检查的build研究或本机恢复日志。清理发布目录前核对运行进程与开机入口，不删除用户AppData恢复记录。
