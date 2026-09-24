# 热门曲库扩充记录 · v0.2.0-preview.2

本轮新增 **48 首不同曲目**，保留原有 10 首来源索引；加上 3 首原创示例，桌面应用共显示 **61 首**。新谱首次从作者的固定 GitHub 提交下载，在本机转换成 TXT；下载完成后可离线使用。

## 数据与验证的含义

50 首社区候选中 48 首通过；原有 10 首再次通过本程序解析。新增谱均核对 UTF-8、原始大小、SHA-256、BPM、音域、事件数、总拍数、总时长和转换结果 SHA-256。C# 转换输出与独立 Python 有理数解析逐个比较音高、休止、时值和顺序，一致后才收录。源码只根据公开格式独立实现，没有合并上游播放器代码。

**这些验证证明转换保留了来源谱；不证明来源编配与原曲录音逐音一致。**逐首原曲听校仍未完成，所有游戏内效果均未测试。旧版原播放器验证记录保留原日期与范围。

条目统计：完整版（仅按来源标题标注） 1 首；片段 29 首；副歌片段 1 首；来源版本、完整性未核对 17 首。保留每个来源文件的全段，不裁短凑数；养鼠鼠小曲只有 5.6 秒，明确标为片段。

## 来源与分发

- 选曲参考 B 站、已登录抖音及社区曲库。热度数字、链接与候选缺口见 `catalog/research.json`，B 站播放量不与抖音点赞量混排。
- 实际新增文件来自 `JikoSchnee/delta-harmonica-macro` 固定提交 `9072cc92e3bd88d5a51c694335d6243734530a09`。上游 README 明确说明社区曲谱不自动适用代码许可证，因此应用与公开索引仓库均不镜像这些谱面。
- 公开仓库 <https://github.com/Atetra2007/delta-force-harmonica-scores> 只分发来源索引、整理说明及本项目原创测试谱。个人曲谱、下载缓存、听校素材与私有仓库代码均不上传。
- 原来源失效时显示下载失败并允许重试；已下载曲谱仍可离线使用。朋友下载歌曲无需 GitHub 登录，应用安装包仍按私有仓库原权限分发。

## 尚未加入

- 起风了：来源含时长不足 10 毫秒的音符；不偷偷延长音符或丢弃音符。
- 情书 Luv Letter：来源第 32 行有超出当前口琴音域的音符；不自动改变八度。
- See You Again：已找到的文本缺逐音时值。昔涟、强军战歌、红尘客栈等缺少本轮可校验的稳定文件。
- 原作者的新 ZIP 在网盘可见，但内容尚未取得；没有把包名或作者宣称数量当作导入成功。千本樱、诀别书采用本轮核查的另一社区来源。

## 新增歌单

| 曲名 | 版本 | 时长 | BPM | 来源提供者 |
|---|---|---:|---:|---|
| [未闻花名](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%9C%AA%E9%97%BB%E8%8A%B1%E5%90%8D-1-19thill-c1539735cd.deltamusic) | 来源版本 | 4:18 | 88 | 19thill |
| [红凯之歌](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E7%BA%A2%E5%87%AF%E4%B9%8B%E6%AD%8C-%E6%AC%A7%E5%B8%83%E5%A5%A5%E7%89%B9%E6%9B%BC-%E5%B0%8F%E8%A5%BF%E8%B4%B5%E9%9B%84-worldLink-e74ffb220b.deltamusic) | 来源版本 | 3:10 | 88 | worldLink |
| [养鼠鼠的小曲](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%BC%A0%E9%BC%A0%E7%9A%84%E5%B0%8F%E6%9B%B2GHUB-11116-leifeng-ffb7708050.deltamusic) | 片段 | 0:06 | 300 | leifeng |
| [花海](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%8A%B1%E6%B5%B7-%E5%91%A8%E6%9D%B0%E4%BC%A6-dhmt-8006ad600d.deltamusic) | 来源版本 | 4:24 | 75 | dhmt |
| [樱花草](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%A8%B1%E8%8A%B1%E8%8D%89-%E8%8A%B1%E8%A8%80%E4%B9%94%E8%AF%AD-Holen-3b69e979cf.deltamusic) | 片段 | 1:15 | 68 | Holen |
| [安河桥](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%AE%89%E6%B2%B3%E6%A1%A5-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-c112e7b4b3.deltamusic) | 片段 | 1:03 | 65 | 癫公灬 |
| [海阔天空](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%B5%B7%E9%98%94%E5%A4%A9%E7%A9%BA-Beyond-hewu-f95ac5ea2b.deltamusic) | 片段 | 1:32 | 110 | hewu |
| [老男孩](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%80%81%E7%94%B7%E5%AD%A9-%E7%99%AB%E5%85%AC%E7%81%AC-%E7%99%AB%E5%85%AC%E7%81%AC-ee7090e07ef1f2aeea130.deltamusic) | 片段 | 1:08 | 72 | 癫公灬 |
| [千本樱](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%8D%83%E6%9C%AC%E6%A8%B1-%E5%88%9D%E9%9F%B3%E6%9C%AA%E6%9D%A5-Jiko-7b66886f2d.deltamusic) | 片段 | 0:50 | 154 | Jiko |
| [诀别书](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%AF%80%E5%88%AB%E4%B9%A6.deltamusic) | 片段 | 0:38 | 120 | 呜呼 |
| [生生世世爱](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E7%94%9F%E7%94%9F%E4%B8%96%E4%B8%96%E7%88%B1-1-19thill-9e6fa6fcf8.deltamusic) | 片段 | 1:32 | 134 | 19thill |
| [国际歌](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%9B%BD%E9%99%85%E6%AD%8C-%E4%BD%9A%E5%90%8D-Jiko-f0814657db.deltamusic) | 来源版本 | 2:32 | 98 | Jiko |
| [歌唱祖国](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%AD%8C%E5%94%B1%E7%A5%96%E5%9B%BD-%E4%B8%AD%E5%9B%BD%E5%B9%BF%E6%92%AD%E8%89%BA%E6%9C%AF%E5%9B%A2%E5%90%88%E5%94%B1%E5%9B%A2%E6%BC%94%E5%94%B1-Meimei-20b83f42b7.deltamusic) | 片段 | 0:55 | 128 | Meimei |
| [蜜雪冰城](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%9C%9C%E9%9B%AA%E5%86%B0%E5%9F%8E-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-c7f2e573c3.deltamusic) | 片段 | 0:18 | 104 | 癫公灬 |
| [太阳照常升起](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%A4%AA%E9%98%B3%E7%85%A7%E5%B8%B8%E5%8D%87%E8%B5%B7-%E4%B9%85%E7%9F%B3%E8%AE%A9-Jiko-297b50d297.deltamusic) | 片段 | 0:49 | 128 | Jiko |
| [送别](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%80%81%E5%88%AB-%E6%9C%B4%E6%A0%91-Jiko-12154b027b.deltamusic) | 片段 | 1:35 | 80 | Jiko |
| [龙卷风](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%BE%99%E5%8D%B7%E9%A3%8E-%E5%91%A8%E6%9D%B0%E4%BC%A6-Jiko-f73b7c0527.deltamusic) | 来源版本 | 2:27 | 72 | Jiko |
| [荒](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%8D%92-%E9%83%91%E9%B1%BC-Hamty-d278aa3ff8.deltamusic) | 片段 | 0:40 | 100 | Hamty |
| [七里香](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E4%B8%83%E9%87%8C%E9%A6%99-%E5%91%A8%E6%9D%B0%E4%BC%A6-dhmt-8c538e936a.deltamusic) | 来源版本 | 4:06 | 125 | dhmt |
| [枫](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%9E%AB-%E5%91%A8%E6%9D%B0%E4%BC%A6-dhmt-8c78a83592.deltamusic) | 来源版本 | 4:11 | 65 | dhmt |
| [青鸟](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%9D%92%E9%B8%9F-%E7%94%9F%E7%89%A9%E8%82%A1%E9%95%BF-Jiko-60df2692f2.deltamusic) | 片段 | 1:24 | 150 | Jiko |
| [天空之城](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%A4%A9%E7%A9%BA%E4%B9%8B%E5%9F%8E-%E4%B9%85%E7%9F%B3%E8%AE%A9-Jiko-3d39404450.deltamusic) | 片段 | 1:29 | 87 | Jiko |
| [逍遥叹](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%80%8D%E9%81%A5%E5%8F%B9-%E8%83%A1%E6%AD%8C-wobushigesheng-a42abb1cf8.deltamusic) | 来源版本 | 2:14 | 72 | wobushigesheng |
| [雪之华](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E9%9B%AA%E4%B9%8B%E5%8D%8E-%E4%B8%AD%E5%B2%9B%E7%BE%8E%E5%98%89-Jiko-803ba1ac7b.deltamusic) | 来源版本 | 5:30 | 60 | Jiko |
| [Lemon](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/Lemon-%E5%AE%8C%E6%95%B4%E7%89%88-%E7%B1%B3%E6%B4%A5%E7%8E%84%E5%B8%AB-Jiko-623a5950c3.deltamusic) | 完整版（来源标注） | 4:03 | 87 | Jiko |
| [贝加尔湖畔](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%B4%9D%E5%8A%A0%E5%B0%94%E6%B9%96%E7%95%94-%E6%9D%8E%E5%81%A5-Jiko-0a301a67a5.deltamusic) | 片段 | 1:57 | 76 | Jiko |
| [女儿情](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%A5%B3%E5%84%BF%E6%83%85-%E5%90%B4%E9%9D%99-YANG-1c83d5abb4.deltamusic) | 片段 | 1:01 | 90 | YANG |
| [一生所爱](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E4%B8%80%E7%94%9F%E6%89%80%E7%88%B1-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-06623ea111.deltamusic) | 片段 | 1:01 | 65 | 癫公灬 |
| [南山南](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%8D%97%E5%B1%B1%E5%8D%97-%E7%99%AB%E5%85%AC%E7%81%AC-%E7%99%AB%E5%85%AC%E7%81%AC-13ca7faa29ee95c7cd0ad.deltamusic) | 来源版本 | 2:24 | 70 | 癫公灬 |
| [我好像在哪见过你](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%88%91%E5%A5%BD%E5%83%8F%E5%9C%A8%E5%93%AA%E8%A7%81%E8%BF%87%E4%BD%A0-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-a164108cd0.deltamusic) | 来源版本 | 2:44 | 110 | 癫公灬 |
| [月半小夜曲](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%9C%88%E5%8D%8A%E5%B0%8F%E5%A4%9C%E6%9B%B2-%E6%9D%8E%E5%85%8B%E5%8B%A4-hewu-f64bfd64a6.deltamusic) | 片段 | 1:29 | 130 | hewu |
| [皇后大道东](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E7%9A%87%E5%90%8E%E5%A4%A7%E9%81%93%E4%B8%9C-%E7%BD%97%E5%A4%A7%E4%BD%91-Jiko-f8af55ca1d.deltamusic) | 片段 | 1:38 | 118 | Jiko |
| [土耳其进行曲](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%9C%9F%E8%80%B3%E5%85%B6%E8%BF%9B%E8%A1%8C%E6%9B%B2-%E8%8E%AB%E6%89%8E%E7%89%B9-Jiko-0fc06d75c0.deltamusic) | 来源版本 | 2:33 | 100 | Jiko |
| [斯卡布罗集市](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%96%AF%E5%8D%A1%E5%B8%83%E7%BD%97%E9%9B%86%E5%B8%82-%E8%8E%8E%E6%8B%89%E5%B8%83%E8%8E%B1%E6%9B%BC-Jiko-60b221025d.deltamusic) | 片段 | 1:03 | 100 | Jiko |
| [City of Stars 爱乐之城主题曲](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/City-of-Stars-%E7%88%B1%E4%B9%90%E4%B9%8B%E5%9F%8E%E4%B8%BB%E9%A2%98%E6%9B%B2-%E9%9B%B7%E7%B1%B3%E5%85%8B%E6%96%AF-Meilichios-73624cf131.deltamusic) | 来源版本 | 2:17 | 105 | Meilichios |
| [YMCA](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/YMCA-Village-People-Jiko-ac8f24d385.deltamusic) | 来源版本 | 2:24 | 127 | Jiko |
| [上海滩](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E4%B8%8A%E6%B5%B7%E6%BB%A9-%E5%8F%B6%E4%B8%BD%E4%BB%AA-Jiko-9f506c4d34.deltamusic) | 来源版本 | 2:44 | 89 | Jiko |
| [再见](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%86%8D%E8%A7%81-%E5%BC%A0%E9%9C%87%E5%B2%B3-YANG-7e54e92291.deltamusic) | 片段 | 1:10 | 110 | YANG |
| [当年情](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%BD%93%E5%B9%B4%E6%83%85.deltamusic) | 片段 | 0:24 | 80 | Jiko |
| [天外来物](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%A4%A9%E5%A4%96%E6%9D%A5%E7%89%A9-%E5%89%AF%E6%AD%8C-%E8%96%9B%E4%B9%8B%E8%B0%A6-ZhGr-5374cfda3e.deltamusic) | 副歌片段 | 1:12 | 78 | ZhGr |
| [最佳损友](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E6%9C%80%E4%BD%B3%E6%8D%9F%E5%8F%8B-%E7%AE%80%E7%9F%AD%E7%89%88-%E9%99%88%E5%A5%95%E8%BF%85-QvQemmm-aa0562e1b5.deltamusic) | 片段 | 1:03 | 80 | QvQemmm |
| [路小雨](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E8%B7%AF%E5%B0%8F%E9%9B%A8-%E5%91%A8%E6%9D%B0%E4%BC%A6-dhmt-6a9540c156.deltamusic) | 片段 | 1:27 | 77 | dhmt |
| [天使爱美丽](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%A4%A9%E4%BD%BF%E7%88%B1%E7%BE%8E%E4%B8%BD-Yann-Tiersen-Jiko-d2eeee4897.deltamusic) | 来源版本 | 2:06 | 93 | Jiko |
| [今宵多珍重](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E4%BB%8A%E5%AE%B5%E5%A4%9A%E7%8F%8D%E9%87%8D-%E9%99%88%E7%99%BE%E5%BC%BA-Jiko-47538fbf90.deltamusic) | 片段 | 1:07 | 125 | Jiko |
| [勉为其难](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%8B%89%E4%B8%BA%E5%85%B6%E9%9A%BE-%E7%8E%8B%E5%86%95-zmccc-7bcd80bdc2.deltamusic) | 片段 | 0:32 | 68 | zmccc |
| [乌兰巴托的夜](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E4%B9%8C%E5%85%B0%E5%B7%B4%E6%89%98%E7%9A%84%E5%A4%9C-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-3976eef92c.deltamusic) | 片段 | 0:30 | 65 | 癫公灬 |
| [哀与悲](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%93%80%E4%B8%8E%E6%82%B2-%E7%99%AB%E5%85%AC%E7%81%AC-372193988-c87fbf7889.deltamusic) | 片段 | 1:33 | 65 | 癫公灬 |
| [宁次之死](https://github.com/JikoSchnee/delta-harmonica-macro/blob/9072cc92e3bd88d5a51c694335d6243734530a09/data/community-scores/%E5%AE%81%E6%AC%A1%E4%B9%8B%E6%AD%BB-%E7%81%AB%E5%BD%B1%E5%BF%8D%E8%80%85-Jiko-5142338691.deltamusic) | 来源版本 | 3:05 | 68 | Jiko |

## 开发与验收

- `CommunityScoreConverter` 在下载环节转换社区格式，播放器、编辑器和个人导入仍使用 TXT。无 MP3、图片、视频或通用 MIDI 导入承诺。
- 先校验源文件，再校验转换结果并解析成功，最后原子保存；取消和失败不创建可用条目。已存在个人文件不覆盖。
- 稳定来源 ID 保留收藏、最近使用、个人标题与谱面；新格式元数据为可选字段，兼容旧目录。未知个人设置原样保留。
- 独立审核命令：运行 `Harmonica.Desktop.Tests -- --audit-catalog catalog/songs.json local/catalog-cache local/catalog-run.json --fetch`，然后执行 `python scripts/audit_community.py catalog/songs.json local/catalog-cache local/catalog-run.json`。本地报告包含事件，不能提交；公开报告只含统计与摘要。
- 自动化验收：102 项核心测试、18 项 Windows 输出模拟测试、15 项桌面存储场景和 10 项准备/打包测试通过；58 首文件审计、48 首独立逐事件比对及发布清单检查通过。输出模拟测试不代表真实游戏测试。
- 真实窗口初查：新建独立测试数据目录显示 61 首；默认本地试听；搜索“欧布小曲”只匹配“红凯之歌”，下载前显示来源版本与 03:10 时长。
- 显式 `--data-dir` 测试目录各自使用独立实例锁；普通桌面启动仍维持单实例。另一任务的跟谱开发窗口和未提交源码均保持原状。
- 发布安装与后续 GUI 检查记录随 Release 的验收说明提供。干净 Windows 和真实朋友试用仍为未验证。
