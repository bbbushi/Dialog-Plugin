# Otus Dialogue System

数据驱动的 Unity 对话系统，面向"策划可独立工作"的工具链。

## 安装

前置：Unity 2022.3 LTS。本仓库是 **UPM 包仓库**（仓库根 = Unity 工程的 Packages 目录内容），**不能作为工程直接用 Unity Hub 打开**。

- **方式一 · Package Manager 本地安装（推荐）**：`Window > Package Manager > + > Install package from disk`，选择仓库内 `com.otus.dialogue/package.json`
- **方式二 · git URL 直装**：Package Manager → `+` → `Install package from git URL`，输入 `https://github.com/bbbushi/Dialog-Plugin.git?path=/com.otus.dialogue`
- 装好后到 Package Manager 的 Samples 里 Import「Basic Demo」，再执行下面的 `Dialogue > Setup All`

## 快速开始

1. 导入本包后，菜单 `Dialogue > Setup All`：
   - 生成中文 TMP 字体（Dynamic 模式，从系统 SimHei/微软雅黑复制；商用请自备授权字体）
   - 生成全局样式 `Assets/Resources/DialogueUIConfig.asset`
   - 若项目装了 Paper UI 素材包则自动接线纸质贴图，否则使用纯色兜底（可在 config 里手动指定）
   - 生成 Demo 对话并接线场景中的 Cube（可选）
2. 导入 Samples 的 BasicDemo 得到示例对话与角色
3. 策划工作流：选中 Dialogue 资产 → 卡片编辑器（下一句下拉防断链）→ `Dialogue > Open Preview` 免 Play 试玩 → 保存时自动校验

## 开发者引导（改包代码）

1. 新建 Unity 2022.3 工程（或用现有工程）
2. 把 `com.otus.dialogue/` 拷入工程的 `Packages/`（embedded package），即可开发
3. 仓库根的 `manifest.json` / `packages-lock.json` 是开发期依赖参考，不要直接覆盖自己工程的 manifest
4. 跑测试：若用 embedded package 方式，把 `"testables": ["com.otus.dialogue"]` 加进自己工程的 manifest，再用 `Window > General > Test Runner` 跑 EditMode

## 模块

| 模块 | 说明 |
|---|---|
| Runtime/Dialogue.asmdef | 数据层（DialogueAsset/Node/SpeakerAsset/UIConfig）+ 运行时（Manager 状态机打字机 / UI 自建 / Validator） |
| Editor/Dialogue.Editor.asmdef | 卡片 Inspector、免 Play 预览窗、样式编辑器、Setup 工具、保存校验钩子 |
| Tests/Editor | EditMode 单元测试（数据跳转/校验规则/Speaker 回退链/样式解析） |

## 约定与注意

- **用户数据全部在项目侧**（Assets/Resources 的样式、Assets/Dialogue 的对话与角色、Assets/Fonts 的字体）——升级本包不会覆盖任何用户数据
- Speaker 资产：右键 `Create > Dialogue > Speaker`（显示名/头像/名字颜色覆盖；无头像自动显示名字首字）
- 每对话样式：Dialogue 资产 Inspector 里「创建新样式…」以全局默认为起点
- 本项目开发者注意：本项目 `Assets/Dialogue/` 与 `Samples~/BasicDemo/` 内容同源同 GUID，**不要在本项目重复导入 Samples**（GUID 冲突）
- 更新发布：改代码 → 提升 `package.json` 的 version → 重新分发；脚本 GUID 不变，用户资产引用不断
