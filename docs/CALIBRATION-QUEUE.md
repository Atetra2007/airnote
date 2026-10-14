# 未复核作品队列

## 当前校准阶段处置

2026-10-14 的当前阶段已完成处置：原固定范围为 **221 个作品组 / 352 个来源**；其中 **339 个来源对应 340 个已通过编配**，剩余 **13 个来源由用户主动移出当前总表**，本阶段待处理来源为 **0**。

阶段结束不等于原范围全部来源音乐通过。主动移出的来源记为 `user-excluded`，没有升级为已通过；同作品的已通过编配继续保留。原材料、历史冻结队列和音乐证据保留在本地，历史台账仍描述当时的音乐验收范围，不能当作当前待办队列。精确处置清单见 [calibration-stage-20261014.json](../catalog/calibration-stage-20261014.json)。人工实际试听未进行，真实游戏验收未进行。

| 已移出的来源 ID | 作品 | 处置原因 |
| --- | --- | --- |
| `zhouzhou-096` | I Really Want to Stay at Your House | `user-excluded` |
| `social-570a13a7905273f2` | 中国人能飞 | `user-excluded` |
| `zhouzhou-163` | 中国人能飞 | `user-excluded` |
| `social-b1fca0af57b20c70` | 做梦的人偶 | `user-excluded` |
| `zhouzhou-175` | 呆呆兽主题曲 | `user-excluded` |
| `alternative-score-4872a0a3ea2efef0` | 念张师 | `user-excluded` |
| `zhouzhou-145` | 怪咖 | `user-excluded` |
| `zhouzhou-085` | 海海海 | `user-excluded` |
| `social-29fa2a6a85a3c996` | 盖亚的力量(ガイアノチカラ) | `user-excluded` |
| `social-909b4b7e44ee481a` | 目标是神奇宝贝大师! | `user-excluded` |
| `social-33b25f8dc6422b65` | 红尘客栈 | `user-excluded` |
| `zhouzhou-181` | 葬花 | `user-excluded` |
| `deck-14a637705cb0` | 送别 | `user-excluded` |

当前目录有 **0 个作品组**没有已复核片段。队列按作品组列出，并附上可在 `catalog/songs.json` 中定位的版本 ID；同组归并可能仍是暂定判断。此表是整理队列，不意味着条目已校准或可用于演奏。

暂缓版本会在版本 ID 后标注 `（暂缓）`；这类条目仍保留在队列中，但不代表当前优先复核。未知 BPM 保持未知。

| # | 作品 | work_id | 版本数 | 版本 ID |
| ---: | --- | --- | ---: | --- |

逐音复核应绑定具体版本和文件哈希，并明确实际核对的片段起止位置。不要因同组其他版本已复核、来源时值已核对或 BPM 可用而升级标签。

本文件由 `scripts/catalog_status.py` 生成；运行 `python scripts/catalog_status.py --check` 检查是否陈旧。
