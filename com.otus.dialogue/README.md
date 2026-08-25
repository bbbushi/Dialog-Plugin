# Otus Dialogue System

数据驱动的 Unity 对话系统，面向"策划可独立工作"的工具链。

## 快速开始

1. 导入本包后，菜单 `Dialogue > Setup All`：
   - 生成中文 TMP 字体（Dynamic 模式，从系统 SimHei/微软雅黑复制；商用请自备授权字体）
   - 生成全局样式 `Assets/Resources/DialogueUIConfig.asset`
   - 若项目装了 Paper UI 素材包则自动接线纸质贴图，否则使用纯色兜底（可在 config 里手动指定）
   - 生成 Demo 对话并接线场景中的 Cube（可选）
2. 导入 Samples 的 BasicDemo 得到示例对话与角色
3. 策划工作流：选中 Dialogue 资产 → 卡片编辑器（下一句下拉防断链）→ `Dialogue > Open Preview` 免 Play 试玩 → 保存时自动校验

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
