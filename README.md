<p align="center">
  <img src="docs/images/airnote.png" alt="Airnote 图标" width="88" height="88">
</p>

# Airnote

**面向《三角洲行动》口琴的 Windows 曲谱与练习助手。**

搜一首曲、听一遍旋律，再跟着节奏练起来。Airnote 把曲谱搜索、收藏、离线试听和悬浮窗跟谱放在一起，方便整理曲库，也方便练习喜欢的片段。

*A Windows score library and practice companion for harmonica in Delta Force.*

[下载安装](https://github.com/Atetra2007/airnote/releases/tag/v0.4.3) · [快速上手](docs/QUICKSTART.md) · [跟谱说明](docs/GUIDE.md) · [问题反馈](https://github.com/Atetra2007/airnote/issues/new/choose)

Windows 10 22H2 / Windows 11 x64 · 自带运行环境 · 无需注册软件账号 · 非官方独立项目

## 从选曲到练习

- **找曲更方便**：按歌名、别名、拼音、来源作者或 BPM 搜索，收藏常练曲目，继续最近的练习。
- **先听再练**：用本地合成音试听旋律，支持 0.5×—1.5× 临时变速，无需打开游戏。
- **边看边弹**：用悬浮窗跟着节奏走谱，或选择弹对才继续；支持片段定位与 A/B 循环。
- **自己的曲谱自己管理**：导入或拖入兼容 TXT，编辑后保存或另存副本，个人曲谱保存在本机。

## 当前可下载版本

项目现名 **Airnote**，原名“口琴小曲库”（HarmonicaDesk）。**目前 GitHub 最新安装包为 v0.4.3，仍显示旧名称和旧图标**；本次仓库更名不改变已发布安装包。后续 Airnote 安装包以 Release 的实际附件为准。

v0.4.3 安装后自带 **374 份已复核 TXT，覆盖 244 个作品**，另附 3 份原创测试谱，首次启动即可离线选曲。部分谱面只覆盖指定片段，“已复核”不代表整曲听辨或真实游戏验收全部完成。详情见[曲库状态](docs/CATALOG-STATUS.md)、[发布说明](docs/RELEASE-0.4.3-NOTES.md)和[验证记录](docs/RELEASE-0.4.3-STATUS.md)。

Airnote 是本项目唯一维护入口，源码与 Release 下载公开可访问，下载无需 GitHub 登录；提交反馈需要登录 GitHub。已下载的安装包与内置曲库可离线使用。

旧“三角洲口琴”曲谱仓库已整合到本项目，保留为私有历史归档。当前曲库见 [catalog/songs.json](catalog/songs.json)，原索引与去重依据见[合并记录](docs/REPOSITORY-MIGRATION.md)。

## 第一次使用

1. 从 [v0.4.3 发布页](https://github.com/Atetra2007/airnote/releases/tag/v0.4.3)的 Assets 下载 `HarmonicaDesk-Setup-0.4.3-win-x64.exe`，核对同名 `.sha256` 后安装。使用 Assets 中的安装包，GitHub 的 Source code ZIP 不是可运行软件。
2. 打开“口琴小曲库”，在“本地”中搜索并选择歌曲，保持“本地试听”，点击底部“试听”；点击“停止”或按 F8 结束。
3. 需要看完整谱面时点击顶部“曲谱”或右侧“查看曲谱”。自己的曲谱仍可通过“导入 TXT”添加。

当前总表有 **244 个作品组、780 个版本记录**；随包提供其中全部 **374 份已复核 TXT**，其他来源参考版本保留原有状态，不能理解为 780 份 TXT 全部内置。具体分类见[曲库状态](docs/CATALOG-STATUS.md)。复核状态绑定具体文件哈希，导入不同文件或编辑后不会沿用“已复核”。安装包不包含录音、来源图片或个人曲谱。

[详细快速说明](docs/QUICKSTART.md)包含文件夹导入、旧使用包迁移、设置、数据备份和常见问题。使用已下载的安装包和内置曲库不需要 GitHub 登录或联网。外部曲谱的分发许可状态仍按[来源与许可](THIRD_PARTY.md)记录。

## 这一版可以做什么

| 功能 | 使用方式 |
| --- | --- |
| 内置曲库、搜索、收藏、最近使用 | 安装后直接选曲；星标加入收藏 |
| 曲库视图与筛选 | 全部、收藏、最近视图；可按本地可用、已复核片段、速度待确认筛选 |
| TXT 导入 | 选择文件、拖入 TXT，或在“⋯”中导入文件夹、旧使用包、粘贴曲谱 |
| 看谱和编辑 | 切换“曲谱”页签，长谱分页；显示音高、拍数、BPM 和预计时长，可编辑或另存副本 |
| 本地试听 | C 调合成音，提供试听音量；无需打开游戏，不模拟游戏原声 |
| 主题与热键 | 设置中选择跟随系统／浅色／深色，以及开始、停止快捷键 |
| 游戏演奏 | 普通 Windows 键鼠输入，默认 F6 开始、F8 停止，开始前有 3 秒可取消倒计时 |
| 跟谱手弹 | 八轨半透明悬浮窗；按节奏走谱或弹对才继续，手动按游戏按键 |
| 速度与片段练习 | 试听、自动演奏和跟谱均有 0.5×—1.5× 临时倍率；跟谱支持定位与 A/B 循环，原谱 BPM 不变 |
| 浮窗 | 背景不透明度 35%—95%，位置与大小记忆；演奏时穿透且不抢焦点 |

支持现有 `HarmonicaPlayer/1` 和旧格式数字 TXT。没有标明 BPM 的文件需要确认速度；无法识别的谱面会提示位置。相同内容重复导入会复用已有曲目；同名但内容不同的曲谱保留为独立条目，源文件不会被覆盖。

本版聚焦 TXT 与单首播放。MIDI、MusicXML、图片、视频、MP3 需先在独立流程中整理为兼容 TXT；没有节奏信息的纯数字谱不能自动还原准确歌曲。素材整理和授权规则见[添加曲谱](docs/ADDING_SCORES.md)与[Codex 整理模板](templates/codex-transcription-prompt.md)。

## 程序与个人文件

- 程序安装在 `%LOCALAPPDATA%\Programs\HarmonicaDesk`，主程序是 `HarmonicaDesk.exe`。
- 个人曲谱、收藏、最近使用、设置与诊断日志保存在 `%LOCALAPPDATA%\HarmonicaDesk`。备份这个完整目录即可保留个人数据。
- 覆盖安装和卸载按设计保留个人数据；安装、卸载前正常关闭程序。卸载不等于删除自己的曲谱。
- 重复打开会激活已有窗口；每次启动默认选择本地试听，不自动播放。没有开机自启、后台更新服务、云端账号或投稿服务器。

这是基于 C# / .NET 10 / WPF **独立编写的新程序**。未并入上游 HarmonicaPlayer 的源码或 EXE；上游仅作为固定曲谱格式、来源索引与历史参考。程序及本项目原创测试谱采用 MIT 许可，外部曲谱的授权单独判断，详见[来源与许可](THIRD_PARTY.md)。

## 验证与使用边界

v0.3 跟谱的[使用说明](docs/GUIDE.md)与[验证记录](docs/GUIDE-VALIDATION.md)分别列明操作和证据。窗口化、无边框游戏为支持目标；独占全屏不保证覆盖。本机已由用户确认自动演奏三个长音、F8 立即停音、浮窗走谱和切出暂停／手动恢复，详见[游戏验证记录](docs/GAME-VALIDATION.md)。这不代表全部组合键、完整歌曲或跨屏验收通过。

新桌面程序的构建、测试和真实界面结果以[桌面版验证记录](docs/DESKTOP-VALIDATION.md)为准。自动测试验证解析、时间轴、停止和故障清理等行为，不能替代干净 Windows 环境、真实朋友试用或游戏实测。包体、启动时间和内存分别记录，不能仅凭界面或包体大小宣称低占用。

游戏自动输入仍有违规和封禁风险。[腾讯公告](https://df.qq.com/cp/a20240906main/newsdetail.html?id=3109167722633874126)列有自动脚本、鼠标宏及相关硬件，未找到口琴演奏豁免说明。普通输入、独立实现和本机游戏测试都不能证明账号安全；本项目不提供驱动、反检测或硬件规避方案，朋友首次使用验证仍未完成。

本地试听不会发送键鼠输入。游戏模式只有在倒计时结束后识别到三角洲客户端前台窗口才开始；窗口切换、手动修饰键冲突等会停止并释放程序尝试按下的键。热键不可用时游戏模式被禁用；释放失败会显示原因，需要停止重试。

应用与安装包尚未配置代码签名。Release 的 SHA-256 用来核对下载完整性，不是安全认证；出现 Windows 或安全软件提示时先核对来源与具体提示，不修改系统防护设置。

## 反馈与参与

- [报告使用问题](https://github.com/Atetra2007/airnote/issues/new?template=problem.yml)：附软件版本、操作步骤、预期与实际结果。
- [提出功能建议](https://github.com/Atetra2007/airnote/issues/new?template=feature.yml)：说明练习或整理曲谱时遇到的具体困难。
- [提交曲谱纠错或来源](https://github.com/Atetra2007/airnote/issues/new?template=score.yml)：写明曲名、版本、问题段落和可核查的来源；许可未确认时只提交链接。

截图和日志请先遮挡账号与个人路径，不提交登录信息或未获许可的谱面全文。贡献代码可先说明问题和复现方式；构建方法见[桌面版构建说明](docs/DESKTOP-BUILD.md)。

## 旧使用包与开发

[v0.1.1-logtest](https://github.com/Atetra2007/airnote/releases/tag/v0.1.1-logtest)仍保留：它是下载并管理原版 HarmonicaPlayer v0.2.1 的朋友使用包，与本次新桌面程序不同。原版曾完成三首样本的整段日志测试，详见[历史原版验证记录](docs/VALIDATION.md)；这些结果不计入新程序验收。旧包中的 `Prepare.cmd`、`index.html` 继续服务原流程，新版安装后不需要运行它们。

已有旧包时，在新程序左侧“⋯ → 导入文件夹 / 旧使用包”选择旧包目录。程序复制其中的个人与候选 TXT 到新曲库，保留旧包文件。

[桌面版构建说明](docs/DESKTOP-BUILD.md) · [旧使用包维护说明](docs/MAINTAINING.md) · [更新记录](CHANGELOG.md) · [历史朋友试用表](docs/FRIEND_TRIAL.md)

[项目介绍与分享文案](docs/PROJECT-PRESENTATION.md) · [仓库整理记录](docs/REPOSITORY-STATUS.md)

发布内容使用明确清单：本项目程序、自带运行时、许可、来源索引、374 份已复核 TXT 和 3 份原创测试谱。上游 EXE、上游参考源码、个人谱面、录音、来源图片和原始验证记录均不加入安装包。
