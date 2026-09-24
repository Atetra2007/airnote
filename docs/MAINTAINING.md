# 维护与发布

v0.2 桌面版的构建入口见 [桌面构建说明](DESKTOP-BUILD.md)，验收见 [桌面验证记录](DESKTOP-VALIDATION.md)。朋友使用安装 EXE，不需要 Python 或 .NET SDK。新程序验收只写入 `catalog/desktop-validation.json`，不要改写原版播放器的历史记录。

以下内容仅适用于保留的 v0.1.1 网页使用包。维护命令需要完整仓库源码；旧朋友使用包不包含维护脚本和测试代码。

```powershell
python scripts/check_repo.py
python scripts/package.py
powershell -NoProfile -File scripts/Prepare.ps1 -VerifyOnly
```

`Prepare.ps1 -Only Scores -SongId father,sunny-day,rain-love` 可只准备三个样本（在 PowerShell 中调用）。脚本固定版本和散列，不自动升级、不执行播放器、不改系统策略、不申请管理员权限。现有文件不匹配时停止，先把自己的修改另存到 `local/my-scores/`，不要强行覆盖。

`catalog/songs.json` 为曲谱索引的唯一数据源。修改后执行 `python scripts/build_catalog.py` 更新 `assets/catalog.js`；`check_repo.py` 会发现不同步。改变 URL 时同时核对固定提交、大小、散列及再分发状态，不把 `app_import` 或 `rhythm` 状态自动升级。

`package.py` 只从明确的文件清单构建 `artifacts/harmonica-friends-kit-v0.1.1-logtest.zip`，包含说明、索引、准备脚本和原创测试谱。不会递归打包整个目录。`local/`、第三方 EXE/PDB/DLL、原版 ZIP 和未授权谱面均不得上传；私有仓库也遵守这条项目约定。

发布前跑仓库检查，核对 ZIP 文件清单，再提交 Git。创建 GitHub 预发布时准确区分下载校验、实际导入、完整日志运行、音乐节奏校对和游戏内效果。附件用本项目生成的 ZIP 和 SHA256 文件，不上传原作者软件。不要把部分通过写成全部验证完成。

每次验收更新 `docs/VALIDATION.md` 与不含谱面、个人路径的 `catalog/validation.json`，保留原始证据与日期。原始界面记录只放在个人 `local/offline-validation/`；不要直接公开整个文件对话框记录。只有逐首完成节奏校对后，才能把相应曲目的 `rhythm` 改为 `reviewed`。公开接收新谱时填写许可依据；无法确认时仅收来源链接。
