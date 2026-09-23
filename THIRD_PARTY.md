# 来源与分发边界

本项目不是 HarmonicaPlayer 官方发行版，也不修改或再分发其程序。`LICENSE` 仅授予本项目原创文件的 MIT 许可，不为歌曲、原曲编配或外部软件授予许可。

| 项目 | 固定来源 | 本包处理 |
| --- | --- | --- |
| 播放器 | [HarmonicaPlayer v0.2.1](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/releases/tag/v0.2.1) | 由使用者从作者 Release 下载；不存入 Git，不附在本项目 Release 中 |
| 10 首候选谱 | [固定提交的曲谱目录](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/tree/0c3735ed7c261349105ff7fc564f92eddcd922cf/%E7%AE%80%E8%B0%B1) | 公开部分只有元数据与来源链接；原谱下载到个人 `local/scores/` |
| 原创测试谱 | `scores/original/` | 为导入与时值验证编写的短序列，随本项目 MIT 许可发布 |

2026-09-24 检查 v0.2.1 根目录未发现明确许可证。这并不等于作者授予了任意修改、改名或再分发的权利。公开网页能阅读、代码有许可证、歌曲可以收听，也都不能单独证明谱面可以再分发。

每首曲谱的源文件名、固定提交、Git blob SHA-1、SHA-256 和来源 URL 保存在 `catalog/songs.json`。其中 `indexed_by` 只表示索引整理者，不表示编曲者。无法从源文件确认的编配者保持“待确认”。

获得明确授权后，可以增加许可依据、链接或本地保存的授权记录，并单独审查可公开的谱面。涉及私人对话的授权证据不要未经同意公开。当前不把这 10 首原谱打成可转发曲谱包；朋友分别从作者公开来源获取。
