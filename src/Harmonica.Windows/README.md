# Windows 输出模块

独立实现，不包含第三方播放器源码。目标 `net10.0-windows`，无 NuGet 依赖。

- `PlaybackEngine`：C4 本地合成试听、日志和游戏输入共享绝对时间轴。默认音量 `0.2`。`Progress.State` 为 `Countdown / Playing / Completed / Stopped / Error`，`EventIndex` 从 0 开始，倒计时为 -1，完成为事件总数。
- `PlayAsync` 重复开始立即拒绝。游戏模式先进行 3 秒可取消倒计时，倒计时后等启动热键释放，最多再等 2 秒。每 10 ms 检查取消和输入前台条件。停止使用 `await StopAsync()`，关闭使用 `await DisposeAsync()`。
- UI 回调在后台线程触发；应使用 `Dispatcher.BeginInvoke` 或 `InvokeAsync`，避免阻塞式 Dispatcher.Invoke 与同步 Dispose 相互等待。
- 只有前台进程名为 `DeltaForceClient-Win64-Shipping` 时才建立游戏输入会话。前台窗口或进程改变、手动修饰键冲突会停止；鼠标修饰键还要求光标处于目标窗口内。没有读游戏内存、驱动、管理员提权或反检测措施。
- 清理仅释放本次尝试按下的键，逆序释放，每次最多重试 3 次。失败保留未释放资源，阻止新播放，再次停止会重试并报告。`NeedsCleanup` 表示仍需保留停止/重试入口。清理不使用已取消的 token。
- `HotkeyManager` 绑定 WPF 窗口句柄并接收 `WM_HOTKEY`。只支持不同的 F1–F12 键，可加 Ctrl/Alt/Shift。停止热键优先注册，部分失败则全部注销。UI 必须在注册失败时禁用游戏模式。
- WinMM 播放自己生成的小型循环 PCM 缓冲，每首结束关闭设备；不包含音色模型，不下载资源。这里只提供旋律检查音色，不能宣称与游戏原声相同。

测试命令（不会发送真实键鼠或音频）：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/Harmonica.Windows.Tests -c Release
```

`src/Harmonica.InputProbe` 是开发验收窗口，不应包含在朋友安装包内。构建后手动打开，点击“开始本窗口测试”并把鼠标留在深色接收区。它只允许向当前进程自己的前台窗口发送，默认不运行任何测试。预计接收键盘 6 次按下/6 次释放、鼠标 4 次按下/4 次释放，可导出 JSON 证据。它不证明游戏兼容性。
