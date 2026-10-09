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

`Assets/Resources/Fonts/SimHei.ttf` 为系统中文字体（黑体），
仅用于本地开发与演示环境的中文显示。若需对外分发，请替换为可再分发的开源中文字体
（如思源黑体 Source Han Sans、Noto Sans SC，均为 SIL OFL 授权）。
代码中已实现回退链：工程字体 → 系统字体 → Unity 内建字体。
