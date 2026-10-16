# 口琴小曲库 v0.4.1-preview.1

这是 0.4.1-preview.1 预发布版，下载入口在 [GitHub Release](https://github.com/Atetra2007/airnote/releases/tag/v0.4.1-preview.1)。仓库为私有，下载需要仓库访问权限。请从该 Release 的 Assets 下载，并用同批 manifest 与 SHA-256 文件核对；不要用本机早前预览包的哈希校验 Release 附件。

本版增加“本地可用／已复核片段／速度待确认”筛选，并保留全部、收藏、最近视图；曲目页显示复核范围和参考录音位置，新增只读短音演奏检查。未知 BPM 共 273 个版本：266 个没有 `bpm` 字段，7 个明确为 `null`，不会以默认速度补值。

虫儿飞、晴天、雨爱和蒲公英的约定四段本轮只做了文件哈希、导入、解析与时序技术复验，听音确认仍待用户完成。B 批四首的证据关系已盘点，音乐核准仍待处理。没有完成真实游戏实测，也没有验证账号安全。

下载 `HarmonicaDesk-Setup-0.4.1-preview.1-win-x64.exe` 可覆盖升级既有安装；便携使用则完整解压 `HarmonicaDesk-0.4.1-preview.1-win-x64.zip`，再运行 `App/HarmonicaDesk.exe`。安装器将程序与个人资料分开存放，按既有设计保留本机曲谱、收藏、最近使用和设置。

源代码内的[本地状态记录](RELEASE-0.4.1-STATUS.md)保留早前预览快照；本次 CI 构建、实际安装和数据保留的最终结果以 GitHub Release 说明中的发布验收记录为准。干净 Windows 与朋友首次使用仍需另行验证。
