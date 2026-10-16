# 来源、许可与分发边界

Airnote（原名“口琴小曲库”）是本项目独立编写的 Windows 应用，不是 HarmonicaPlayer 官方发行版，也不包含其源码或 EXE。`LICENSE` 仅授予本项目原创代码、说明、图标和测试谱的 MIT 许可，不为外部歌曲、编配、谱面或软件授予许可。

## 随安装包分发的内容

| 内容 | 来源与许可 | 处理方式 |
| --- | --- | --- |
| Airnote 源码、界面、图标、辅助脚本与原创说明 | 本项目，[MIT](LICENSE) | 源码在本仓库，桌面程序集与原创资源随安装包发布 |
| 3 份原创测试谱 | `scores/original/`；本项目 MIT | 为音高、时值和导入验证编写的短序列，随包发布 |
| 374 份已复核 TXT，覆盖 244 个作品 | `scores/bundled/`；逐版本来源与复核记录见 `catalog/songs.json` | v0.4.3 根据用户选择随包提供，安装目录为 `Resources/BundledScores/`；外部谱面的 `redistribution=permission-unconfirmed` 状态原样保留，不适用本项目 MIT |
| .NET 与 Windows Desktop 运行时 | Microsoft 的 [.NET](https://github.com/dotnet/runtime) 与 [WPF](https://github.com/dotnet/wpf)；各自许可及第三方声明 | 自包含 Windows x64 发布带入所需运行时；实际运行时包的 `LICENSE.TXT` 与 `THIRD-PARTY-NOTICES.TXT` 复制到安装目录 `Licenses/` |
| 曲谱来源索引与复核摘要 | 本项目整理的曲名、固定地址、BPM、大小与散列 | 现行目录含 244 个作品、780 个版本记录；未随包提供的来源版本仍保留实际状态 |

试听使用本项目生成的 PCM 合成音和 Windows 自带 WinMM 接口，没有第三方音色样本、AI 模型或 NAudio 依赖。键鼠输入使用 Windows 自带 API；没有安装输入驱动。

[Inno Setup](https://github.com/jrsoftware/issrc) 6.7.3 用于构建安装器，遵循其项目许可；编译器不是朋友使用时的依赖。构建使用官方固定发行文件，并验证散列与编译器签名。本项目生成的 EXE 尚未配置代码签名，编译器签名不等于最终安装包已签名。

## 外部曲谱与历史播放器

| 外部内容 | 固定来源 | 获取与保存 |
| --- | --- | --- |
| 早期 10 首来源谱 | [固定提交的曲谱目录](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/tree/0c3735ed7c261349105ff7fc564f92eddcd922cf/%E7%AE%80%E8%B0%B1) | 未内置的来源版本仍可按应用提示获取；用户导入和下载保存在 `%LOCALAPPDATA%\HarmonicaDesk\Scores`，旧准备脚本保存在 `local/scores/` |
| 历史原版播放器 | [HarmonicaPlayer v0.2.1](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/releases/tag/v0.2.1) | 仅旧使用包引导用户从作者 Release 获取；新版不需要它，不存入 Git，不附在本项目 Release 中 |

2026-09-24 检查 HarmonicaPlayer v0.2.1 根目录未发现明确许可证，因此本项目没有复制、改名或再分发其程序和源码。兼容其文本格式不等于继承其代码许可。历史本机验证记录单独保留，也不证明本项目新程序已经通过相同验收。

逐版本的来源文件名、可确认的固定提交、散列、来源 URL 与复核范围保存在 `catalog/songs.json`。`indexed_by` 只表示索引整理者，不表示编曲者。无法从源文件确认的编配者保持“待确认”。散列一致仅说明对应文件一致，不能证明节奏准确或获得再分发授权。

v0.4.3 改为随安装包提供全部 374 份已复核 TXT，原始总量 737,201 字节；这是用户选择的交付方式，不表示原作者授予再分发许可。目录中外部谱面的授权状态继续如实标为 `permission-unconfirmed`。公开可访问、可收听或程序代码采用开源许可证，均不能单独证明谱面可再分发。后续如取得明确授权，应补充可核查的许可依据；私人授权证据不应未经同意公开。

仓库目前为私有，下载 GitHub Release 需要仓库访问权限。用户个人谱面不会自动上传或加入发行；录音、来源图片、上游参考源码、原版 EXE 和原始验证记录也不加入安装包。

## 历史来源记录：v0.2.0-preview.2

当时新增 48 首来源索引来自 JikoSchnee/delta-harmonica-macro 固定提交 9072cc92e3bd88d5a51c694335d6243734530a09。其 README 明确说明曲谱不自动适用代码许可证；当时版本仅在用户点击后从作者原地址获取，并在本机转换。转换器按公开格式独立实现，没有合并上游程序代码。索引仓库的 MIT 许可不覆盖链接所指歌曲，这一许可边界在 v0.4.3 随包提供 TXT 后仍然成立。逐首来源与验证情况见 [来源扩充记录](docs/CATALOG-EXPANSION.md)。
