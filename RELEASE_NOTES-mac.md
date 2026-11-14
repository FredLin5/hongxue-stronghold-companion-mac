# 鸿雪·卫戍助手 · macOS 版

Apple Silicon（arm64）自包含 `.app`，复用原项目的业务逻辑与数据，界面用 Avalonia 重写，
通过 MaaFramework 的 PlayCover 控制单元连接 PlayTools 的 MaaTools 服务读取游戏画面。

## 下载与安装

- 下载 **HongxueStronghold-macOS-arm64-<版本>.zip**（`Source code` 是源码，不能直接运行）。
- 解压后把「鸿雪·卫戍助手.app」拖进「应用程序」。
- 未做公证，首次打开若被 Gatekeeper 拦下：右键 →「打开」，或执行
  `xattr -dr com.apple.quarantine "/Applications/鸿雪·卫戍助手.app"`。
- 需要 macOS 12+ / Apple Silicon，以及 PlayCover 中安装的《明日方舟》。

## 功能

- 干员、装备浏览与筛选；追踪列表（干员/装备、在售标记）
- 本局信息识别：核心/附加盟约禁用、禁用干员、敌方领袖（游戏内放大镜页面，打开即自动识别）
- 商店识别：自动扫描商店，列出本局在售的全部商品，点一下加入追踪
- 游戏内叠加层：透明置顶、鼠标穿透、跟随游戏窗口，追踪目标在商店里整卡高亮
- 阵容：导入森空岛攻略链接，显示阵容的推荐策略
- 阵容规划：选核心/附加盟约的 Ⅰ／Ⅱ 档目标，按 8／9 人 × 0~2 次转职搜索候选；
  可固定必带干员、选本局策略（暴躁铁皮 / 优等生 / 变形同构）、与本局 Ban 联动，
  候选可保存为阵容或直接加入追踪
- 规划页悬停详情：盟约看档位要求、游戏内效果、Ban 说明与全部持有者；
  干员看职业/位置、盾奶叠定位、盟约以及普通 / 精锐各自的特性与技能
- PlayCover（MaaTools）与 ADB 两种连接方式

未移植：站位规划/地图模拟、阵容编辑器、道中路线、自动更新、全局快捷键。
详细说明见仓库 `docs/mac-port.md`。

## 校验

发布页附带 `SHA256SUMS-macos.txt`，可用 `shasum -a 256 -c SHA256SUMS-macos.txt` 校验下载文件。
