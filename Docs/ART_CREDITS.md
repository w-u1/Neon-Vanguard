# 美术资源来源与授权

本工程的美术资源**全部为 CC0（公共领域）**，可自由用于商业与非商业项目，无需署名。
为规范起见，仍在此列明来源。

---

## Kenney · Space Kit 2.0

- **来源**：https://kenney.nl/assets/space-kit
- **作者**：Kenney（https://kenney.nl）
- **授权**：Creative Commons Zero (CC0 1.0) — https://creativecommons.org/publicdomain/zero/1.0/
- **原始授权文件**：见同目录 `kenney_space-kit_License.txt`
- **使用方式**：选取 OBJ + MTL 格式导入到 `Assets/Resources/Models/`

### 本工程实际使用的模型

| 游戏内用途 | 模型文件 | 说明 |
| --- | --- | --- |
| 玩家角色 | `astronautA` | 宇航员，俯视角下辨识度高 |
| 追猎者（普通敌人） | `alien` | 外星生物，体型小、数量多 |
| 暴君（重装敌人） | `machine_generator` | 大型机械，体积感强 |
| 织网者（远程敌人） | `turret_single` | 炮塔造型，天然暗示"远程攻击" |
| 深渊主宰（首领） | `craft_speederD` | 大型飞行器，放大后作为首领主体 |
| 场景景观 | `rock_largeA/B`、`rock_crystals`、`rock_crystalsLargeA`、`rocks_smallA`、`bones`、`craterLarge` | 场地外围的岩石与水晶围栏 |
| 场景地标 | `structure`、`structure_detailed`、`gate_complex`、`satelliteDish_large`、`rocket_baseA` | 四个方向的大型建筑地标 |
| 场内道具 | `barrel`、`machine_generator`、`machine_wireless`、`chimney_detailed` | 掩体装饰，不影响战斗判定 |

### 材质处理说明

Kenney Space Kit 使用纯色材质（`metal` / `metalDark` / `metalRed` / `dark` / `crystal` / `rock` / `skin` 等），
没有贴图。本工程在运行时做了两件事：

1. 对**结构主体材质槽**（`metal`、`metalDark`、`metalRed`）按敌人类型重新染色，
   让每种敌人有明确的颜色身份（红=追猎者、橙=暴君、紫=织网者、深红=首领）。
2. 对**装饰/发光材质槽**（`crystal`、`skin`、`rock` 等）保持 Kenney 原色，只加强自发光，
   避免整只模型被"糊成一块纯色"而丢失细节。

所有染色都是**克隆材质后修改**（`new Material(src)`），不会污染工程内的共享材质资产。

### 未使用的部分

Space Kit 中的走廊、单轨、火箭分段等模块化建筑件未使用 —— 本作是单场景竞技场玩法，
不需要模块化关卡拼接。

---

## 音频

工程内**没有任何音频文件**。所有音效与背景垫乐均由 `Assets/Scripts/Core/AudioKit.cs`
在运行时用数学公式合成（包络 × 振荡器），因此不涉及任何第三方音频授权。

---

## 字体

- **文件**：`Assets/Resources/Fonts/NotoSansSC-VF.ttf`
- **字体**：Noto Sans SC（可变字体，TrueType）
- **授权**：SIL Open Font License 1.1 —— **明确允许随软件打包再分发**
- **版权**：Copyright 2014-2021 Adobe，Reserved Font Name 'Source'；
  Noto 是 Google Inc. 的商标
- **来源**：https://fonts.google.com/noto/specimen/Noto+Sans+SC
- **许可全文**：同目录 `OFL.txt`

### 为什么用它而不用系统黑体

早期版本打包的是 Windows 自带的 `SimHei.ttf`。这有两个问题：

1. **授权**：Windows 自带字体的许可不允许随工程再分发，放进公开仓库有风险；
2. **复现性**：依赖开发机系统字体，其他机器上显示效果无法保证。

Noto Sans SC 是 OFL 授权、可自由再分发，且是**真 TrueType**（sfnt 头 `0x00010000`），
TextMeshPro 对其支持最稳。

### 字形可用性是被断言验证过的

换字体后，自检会向 Unity 字体系统逐个索取界面实际用到的汉字
（`TextRenderer.FindMissingGlyphs`，覆盖中文与 `★×·—` 等符号），
拿到才算通过。**只验证"字体加载成功"是不够的** ——
回退字体或解析失败的字体同样能加载，但中文会全变方块。
这个断言在换字体时确实拦下了一次误判（见 README 第 6 节）。

代码中另有三级回退链：工程字体 → 系统字体 → Unity 内建字体，
因此即使删除字体文件，游戏依然可以启动。
