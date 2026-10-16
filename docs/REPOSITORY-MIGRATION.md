# Airnote 与旧曲谱仓库合并记录

2026-10-16，用户确认以 **Airnote** 为唯一主项目并公开访问，将旧 `delta-force-harmonica-scores` 保留为私有历史归档。本轮整合仓库与维护入口，现行曲库、已发布安装包和个人数据的内容保持原样。

## 统一入口

- 项目、源码与反馈：[Atetra2007/airnote](https://github.com/Atetra2007/airnote)。
- 下载：[v0.4.3 Release](https://github.com/Atetra2007/airnote/releases/tag/v0.4.3)。这是现有旧名称安装包，本次不生成或替换新版安装包。
- 现行目录：[catalog/songs.json](../catalog/songs.json)，780 条版本记录。
- 原创测试谱：[scores/original](../scores/original)，3 份。
- 旧索引：[历史快照](../catalog/history/legacy-sources.json)；逐条处理结果：[迁移清单](../catalog/history/migration.json)。历史快照不参与应用加载，不作为另一套当前曲库。

旧仓库改为私有后，未获访问权限的人无法访问旧地址。两仓库整合不产生自动 URL 跳转；请更新收藏和外部链接到 Airnote。

## 内容对应与去重

| 旧仓库内容 | 处理结果 |
| --- | --- |
| `sources.json` 中 57 条现有来源 | 复用 Airnote 中同 ID 的当前记录，保留已更新的别名、复核状态与说明 |
| 《送别》旧版本 `deck-14a637705cb0` | 保留 `user-excluded` 决定，不重新加入现行目录；依据见 `catalog/calibration-stage-20261014.json` |
| `original/` 中 3 份测试谱 | 与 `scores/original/` 的 Git 文件内容逐字节相同，复用已有文件 |
| `LICENSE` | 两仓库文本逐字节相同，沿用当前 MIT 许可证及外部内容的适用范围说明 |
| 旧 README 与原提交 | 保留在 Git 历史中；当前说明统一到 Airnote README 与本文 |

旧索引共 58 条，处理结果为 **57 条复用、1 条继续排除、0 条新增到现行目录**。3 份原创测试谱不重复加入。现行 780 条记录、374 份随包 TXT 及来源状态保持不变，旧快照不能覆盖当前目录。

## 历史保留与核对依据

- 原仓库 ID：`1384931024`；原提交：`e70df9feedd32d5424a245507e28ff0419585177`。
- Airnote 合并基线：`a0ee32201b5021c4f570d535487a3cc807118e32`。
- 合并提交保留原仓库提交作为第二个父提交，原作者与提交时间保留；不重写 Airnote 现有历史。
- 原索引文件 SHA-256：`5cd638f2fea049bcdbeee60e816d8fee2ac47870351e8233e1cf12029410feed`。
- 合并时现行目录的 Git 内容 SHA-256：`f19e0210d90814781f6f5bf8cff83c332bd5e5e91aa81e64d16066b0277b8b9a`。
- 原创谱的对应文件与 SHA-256，以及每条旧记录的处理依据，均记录在迁移清单中。

两仓库的 Git 历史备份与本地未提交改动备份保留在维护者本机，不加入仓库。公开范围仅包含远端分支、标签及本次合并提交；本地开发分支、暂存区、stash 和工作资料不随此次操作上传。

## 版本、许可与验证范围

本次只整合历史索引、文档和 GitHub 维护入口；v0.4.5 本地开发改动另行保留，不纳入本次提交。安装路径、数据目录、快捷方式、个人曲谱、收藏和设置不作迁移。

现有 9 个 Release 及其附件保持原有内容、版本与 SHA-256。仓库公开不改变外部曲谱的授权状态，`permission-unconfirmed` 等记录原样保留；MIT 许可不自动覆盖外部歌曲或谱面，详见[来源与许可](../THIRD_PARTY.md)。

核对范围为历史与文件对应、曲库去重、凭据及个人文件入仓检查、文档链接、现有自动检查和匿名访问。实际游戏、真人听辨、干净 Windows 安装及本机应用升级不在本轮重新执行。CI 结果见 [GitHub Actions](https://github.com/Atetra2007/airnote/actions)。
