# 来源、许可与分发边界

“口琴小曲库”v0.2 是本项目独立编写的 Windows 应用，不是 HarmonicaPlayer 官方发行版，也不包含其源码或 EXE。`LICENSE` 仅授予本项目原创代码、说明、图标和测试谱的 MIT 许可，不为外部歌曲、编配、谱面或软件授予许可。

## 随安装包分发的内容

| 内容 | 来源与许可 | 处理方式 |
| --- | --- | --- |
| 口琴小曲库源码、界面、图标、辅助脚本与原创说明 | 本项目，[MIT](LICENSE) | 源码在本仓库，桌面程序集与原创资源随安装包发布 |
| 3 份原创测试谱 | `scores/original/`；本项目 MIT | 为音高、时值和导入验证编写的短序列，随包发布 |
| .NET 与 Windows Desktop 运行时 | Microsoft 的 [.NET](https://github.com/dotnet/runtime) 与 [WPF](https://github.com/dotnet/wpf)；各自许可及第三方声明 | 自包含 Windows x64 发布带入所需运行时；实际运行时包的 `LICENSE.TXT` 与 `THIRD-PARTY-NOTICES.TXT` 复制到安装目录 `Licenses/` |
| 10 首曲谱来源索引 | 本项目整理的曲名、固定地址、BPM、大小与散列 | 只分发索引，不附原谱 |

试听使用本项目生成的 PCM 合成音和 Windows 自带 WinMM 接口，没有第三方音色样本、AI 模型或 NAudio 依赖。键鼠输入使用 Windows 自带 API；没有安装输入驱动。

[Inno Setup](https://github.com/jrsoftware/issrc) 6.7.3 用于构建安装器，遵循其项目许可；编译器不是朋友使用时的依赖。构建使用官方固定发行文件，并验证散列与编译器签名。本项目生成的 EXE 尚未配置代码签名，编译器签名不等于最终安装包已签名。

## 外部曲谱与历史播放器

| 外部内容 | 固定来源 | 获取与保存 |
| --- | --- | --- |
| 10 首候选谱 | [固定提交的曲谱目录](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/tree/0c3735ed7c261349105ff7fc564f92eddcd922cf/%E7%AE%80%E8%B0%B1) | 每台电脑由用户点击后，从作者公开来源直接下载；新版保存在 `%LOCALAPPDATA%\HarmonicaDesk\Scores`，旧准备脚本保存在 `local/scores/` |
| 历史原版播放器 | [HarmonicaPlayer v0.2.1](https://github.com/lanselanlanxi-wq/HarmonicaPlayer/releases/tag/v0.2.1) | 仅旧使用包引导用户从作者 Release 获取；新版不需要它，不存入 Git，不附在本项目 Release 中 |

2026-09-24 检查 HarmonicaPlayer v0.2.1 根目录未发现明确许可证，因此本项目没有复制、改名或再分发其程序和源码。兼容其文本格式不等于继承其代码许可。历史本机验证记录单独保留，也不证明本项目新程序已经通过相同验收。

每首候选曲谱的源文件名、固定提交、Git blob SHA-1、SHA-256 和来源 URL 保存在 `catalog/songs.json`。`indexed_by` 只表示索引整理者，不表示编曲者。无法从源文件确认的编配者保持“待确认”。散列一致仅说明内容与固定来源一致，不能证明节奏准确或获得再分发授权。

公开可访问的曲谱、歌曲可以收听、软件代码采用开源许可证，都不能单独证明谱面可再分发。当前不把这 10 首谱面制作成可转发歌曲包，朋友应分别从作者来源获取；个人导入与下载也不会自动上传 GitHub。

获得明确授权后，可补充许可依据与链接，再单独审查是否纳入公开曲库。私人对话中的授权证据不要未经同意公开。用户个人谱面、上游参考源码、原版 EXE 和原始验证记录均不加入本项目源码与安装包。
