# 构建口琴小曲库桌面版

桌面版为原创 .NET 10 WPF 程序，目标 Windows 10 22H2 / Windows 11 x64。安装包包含运行时，朋友不用安装 Python、.NET SDK 或 .NET 运行时。安装器只负责安装，不会启动游戏或发送演奏输入。

## 本机开发与构建

需要 x64 .NET SDK 10.0.300 和 Python 3.11 或更新版本。构建脚本优先使用 `C:\Program Files\dotnet\dotnet.exe`，避免 PATH 中 x86 dotnet 没有 SDK 的问题。依赖锁文件应随项目提交；构建使用 locked restore，不能在发行构建时静默更新依赖。

```powershell
./scripts/Build-Desktop.ps1 -SkipInstaller
```

脚本运行 Core 单元测试、Windows 与 Desktop 的独立验证程序，然后重新还原桌面的 `win-x64` 依赖图，避免测试项目还原覆盖共享工程的 RID 资产。随后发布自包含程序到 `artifacts/desktop/publish`。不裁剪、不合并单文件、不打包调试符号。只会清理这个固定的发布暂存目录，不接触用户曲谱。`-SkipTests` 仅适合已运行测试后的本机迭代；正式发布和 CI 不使用它。

首次开发时，为三个测试项目运行 `dotnet restore <测试工程路径>`，为桌面项目运行 `dotnet restore src/Harmonica.Desktop/Harmonica.Desktop.csproj --runtime win-x64 -p:SelfContained=true`；它们的工程文件已启用锁文件，审查并提交生成的 `packages.lock.json`。不要以全局命令行属性把锁文件生成传播到没有外部依赖的共享库，否则测试和桌面发布的 RID 不同会冲突。发行脚本遇到缺失锁文件会中止。构建时先正常退出开发输出目录中的应用；GUI 验收可使用发布目录的单独副本，避免锁住待编译文件。

当前版本 `0.4.1-preview.1` 通过 [GitHub Release 预发布页](https://github.com/Atetra2007/delta-force-harmonica-kit/releases/tag/v0.4.1-preview.1)提供；仓库为私有，下载需要仓库访问权限。主程序文件名为 `HarmonicaDesk.exe`，产品版本 `0.4.1-preview.1`，Windows 文件版本 `0.4.1.0`。资源必须按确定路径复制：`Resources/catalog.json` 和 `Resources/Examples/` 下三份原创谱；不能用递归整个仓库的方式准备发行内容。

额外的跟谱组件检查：`dotnet run --project tests/Harmonica.LayoutChecks -c Release`。它不创建实际桌面窗口，输出最小／默认尺寸、三种 DPI 的布局图及长谱绘制记录到 `artifacts/guide-validation/components`。使用独立数据做实际 UI 验证：`HarmonicaDesk.exe --data-dir "本机测试目录"`，不要覆盖个人数据。

原生窗口检查：`dotnet run --project tests/Harmonica.LayoutChecks -c Release -- --native`。它创建但不显示 30 个浮窗 HWND，核对分层／穿透／不激活样式、解锁再锁定、屏幕外位置恢复和句柄释放，不注入输入或抢占前台。这不能代替实际游戏的焦点、穿透和跨显示器验收。

物理输入接收器：`dotnet run --project src/Harmonica.InputProbe -c Release -- --guide`。此入口只运行只读监听与等键引擎；开始前不监听，需人工按真实键鼠。它还提供 100 次监听注册／释放自检和证据导出。普通自动化模拟按键不等于物理 Raw Input；原有自动输入接收测试仍是无 `--guide` 的入口。

`-SkipInstaller` 生成的 ZIP 命名为 `HarmonicaDesk-0.4.1-preview.1-win-x64.zip`，并附 SHA-256。ZIP 包含 `App/` 内的自包含程序和根目录使用说明，原始截图、个人数据和测试接收器不进入包。本机旧预览快照不能代表 Release 中 CI 附件；下载或安装时应核对 Release 同批 manifest 与哈希。构建产物本身也不代表完成全曲听校、游戏实测或覆盖升级验收。

## 安装器

使用 Inno Setup **6.7.3**。官方固定发行页：<https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3>。官方仅提供安装程序；本项目不会为了本机构建静默更改系统或自动安装编译器。

如果已经有 Inno Setup 6 编译器：

```powershell
./scripts/Build-Desktop.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

也接受自行准备的 `local/tools/InnoSetup6/ISCC.exe`。没有编译器时使用 GitHub Actions 的 **Build Windows desktop**：工作流在临时 Windows runner 上下载官方固定版本，核对 SHA-256 和 Authenticode，静默安装编译器后打包；不会改变本机系统。官方安装程序 SHA-256 为 `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`。

Release 附件命名：

- `HarmonicaDesk-Setup-0.4.1-preview.1-win-x64.exe`
- 同名 `.exe.sha256`
- `publish-manifest.json`：实际打包文件的名称、大小、SHA-256，无开发机器绝对路径。

CI 产物是否已上传、Release 附件是否完成核验以及安装升级是否通过，以[0.4.1 状态记录](RELEASE-0.4.1-STATUS.md)为准。不要将早前本机预览包的 manifest 或 SHA-256 当作 GitHub Release 文件证据。应用与本项目安装器尚未配置代码签名；编译器签名有效不表示生成的安装器已签名。

## 安装、升级与数据

安装为当前用户，`PrivilegesRequired=lowest`，固定安装位置 `%LOCALAPPDATA%\Programs\HarmonicaDesk`。默认创建桌面快捷方式，并创建开始菜单入口；用户可取消桌面选项，升级保留上次选择。没有自启动项或计划任务。固定 AppId 保证后续版本识别同一产品。

应用个人数据使用独立 `%LOCALAPPDATA%\HarmonicaDesk`。安装器不写入、不迁移、不清理这个数据目录，升级和卸载均保留用户数据。安装和卸载检查 `Local\HarmonicaDesk.App`，正在运行时要求正常退出，不强制终止进程。安装完成后“打开应用”默认不勾选，避免无人值守安装时启动。

## 发布边界与验收

`scripts/check_desktop.py` 根据明确允许的本项目程序集与 .NET 两个运行时包构建文件清单，再校验所有资源与仓库原始文件一致。WPF 卫星资源由运行时包的 `RuntimeList.xml` 与实际已部署的主程序集共同确定，逐文件与 NuGet 包原件核对，不放宽为目录通配。未知 DLL、上游播放器、个人谱面、目录链接、多余文件或缺失依赖都会让打包失败；检查通过后逐文件生成 Inno include，无通配符。安装包保留本项目的许可，以及 Core 运行时提供的 LICENSE / THIRD-PARTY-NOTICES 和 WindowsDesktop 运行时提供的 LICENSE。

```powershell
python scripts/check_desktop.py --self-test
python scripts/check_desktop.py --publish artifacts/desktop/publish
```

自动构建成功不代表完成安装体验验收。正式标记安装验证通过前，仍须实测：无 .NET 的干净 Windows、普通用户安装、桌面快捷方式启动、中文路径、断网启动、覆盖升级后个人曲谱与设置保留、卸载保留个人数据、程序运行时安装/卸载正确要求退出。未实测项保持“未验证”，不把当前构建或历史原版测试当成安装测试结果。
