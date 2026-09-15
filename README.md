# Otus Dialogue System

数据驱动的 Unity 对话系统，面向"策划可独立工作"的工具链。

## 安装

前置：Unity 2022.3 LTS。本仓库是 **UPM monorepo**（核心包 + 两个扩展包，仓库根不是 Unity 工程），**不能直接用 Unity Hub 打开**。

三个包按需安装：

| 包 | 谁装 | 内容 |
|---|---|---|
| `com.otus.dialogue`（核心·必装） | 所有项目 | 数据层 / 运行时 / 全套编辑工具（卡片编辑器、免 Play 预览、节点图、样式编辑器、Setup） |
| `com.otus.dialogue.interaction`（交互扩展） | 通用游戏 | 场景对话触发器（靠近+按键 / 自动播放 / 点击 Demo）+ 触发器管理窗口 |
| `com.otus.dialogue.vn`（视觉小说扩展） | 视觉小说 | 剧本导演（章节串播 / 条件分歧 / Finished 事件）+ 导演管理窗口；后续 BGM / 音效 / 存档 UI 也放这个包 |

- **方式一 · Package Manager 本地安装（推荐）**：`Window > Package Manager > + > Install package from disk`，依次选择所需各包目录内的 `package.json`（核心必选，扩展按需）
- **方式二 · git URL 直装**：Package Manager → `+` → `Install package from git URL`（扩展包声明了对核心的 git 依赖，装扩展会自动带上核心）：
  - 核心：`https://github.com/bbbushi/Dialog-Plugin.git?path=/com.otus.dialogue`
  - 交互扩展：`https://github.com/bbbushi/Dialog-Plugin.git?path=/com.otus.dialogue.interaction`
  - 视觉小说扩展：`https://github.com/bbbushi/Dialog-Plugin.git?path=/com.otus.dialogue.vn`
- 装好后到核心包 Package Manager 的 Samples 里 Import「Basic Demo」，再执行下面的 `Dialogue > Setup All`（Demo 的 Cube 触发器接线需装交互扩展，未装则自动跳过）

## 快速开始

1. 导入本包后，菜单 `Dialogue > Setup All`：
   - 生成中文 TMP 字体（Dynamic 模式，从系统 SimHei/微软雅黑复制；商用请自备授权字体）
   - 生成全局样式 `Assets/Resources/DialogueUIConfig.asset`
   - 若项目装了 Paper UI 素材包则自动接线纸质贴图，否则面板用纯色兜底（可在 config 里手动指定）；姓名牌/头像框底图未设置时不显示（名字文本/头像照常）
   - 生成 Demo 对话并接线场景中的 Cube（可选）
2. 导入 Samples 的 BasicDemo 得到示例对话与角色
3. 策划工作流：选中 Dialogue 资产 → 卡片编辑器（下一句下拉防断链）→ `Dialogue > Open Preview` 免 Play 试玩 → 保存时自动校验
4. 场景接线（需装交互扩展）：菜单 `Dialogue > Trigger Manager`（对话触发器管理器）可视化管理场景中所有对话触发器——给选中物体一键添加、行内换对话/改触发方式、定位物体、免 Play 预览、移除，层级变化自动刷新
5. 视觉小说全局入口（需装 vn 扩展）：菜单 `Dialogue > Director Manager` 管理剧本导演，「＋ 全局入口」挂 `DialogueDirector`（或 `Add Component > Dialogue > Dialogue Director`）——按顺序拖入章节对话资产即自动串播（playOnStart 开场自动开播），每章可写变量条件（不满足自动跳过，同位多条做分歧结局），播完触发 `Finished` 事件（接主菜单/制作名单）
6. 自定义 UI 布局（可选）：默认 UI 由代码搭建、零配置。想自由摆放时：`DialogueUIConfig` Inspector 的「布局」区（或菜单 `Dialogue > UI > 导出当前 UI 为预制体…`）把当前 UI 导出为预制体模板并自动挂回配置——之后在预制体里随意改布局/加装饰/做动画，运行时按节点名解析引用。规则：
   - **别改节点名**：必需节点（Panel、BodyText、ChoiceRoot、HistoryRoot、HistoryScroll、Viewport、Content、InteractPrompt、PromptText）缺任一，运行时自动回退默认布局并在 Console 报错；配置里「检查布局预制体」可一键定位
   - 可选节点（全屏背景/头像框/姓名牌/▼箭头/AUTO 角标/历史窗标题等）删掉只降级对应功能，不报错；全屏背景节点（Background）放层级最底层
   - 摆好的位置不会被运行时冲掉：正文让位（头像出现时右移）、▼箭头呼吸动画都以预制体里的摆放值为基准做相对偏移
   - **动态条目模板**：预制体自带两个未激活的 `ChoiceTemplate`（选项按钮：Button+底图+Label 文本）与 `EntryTemplate`（历史条目：根即文本）——改模板的配色/字号/结构即改对应条目观感；删除模板则该条目回代码生成（配色走配置）。模板需保持未激活；选项文本取「名为 Label 的子节点」优先
   - 嵌套层级自由、可加任意装饰节点；ScrollRect 接线、布局组等**功能组件**缺失会自动补齐，改不坏
   - 皮肤归 DialogueUIConfig 管（字体/九宫格贴图/全屏背景；无模板时的选项配色）。模板存在时条目配色归模板——美术接管；清空「布局预制体」字段即回代码默认
   - **全屏背景**：config 的「全屏背景」区设图即得整屏底图（不设 = 透明，露出游戏画面）；每个对话可在自己的 Inspector 里「创建新样式」覆盖换背景（章节切换场景用）

## 开发者引导（改包代码）

1. 新建 Unity 2022.3 工程（或用现有工程）
2. 把 `com.otus.dialogue/` 拷入工程的 `Packages/`（embedded package），即可开发；要改扩展包就把它需要的包一起拷入（扩展的 Editor 程序集引用核心的 `Dialogue` / `Dialogue.Editor`）
3. 仓库根的 `manifest.json` / `packages-lock.json` 是开发期依赖参考，不要直接覆盖自己工程的 manifest
4. 跑测试：若用 embedded package 方式，把三个包都加进自己工程 manifest 的 testables（`"testables": ["com.otus.dialogue", "com.otus.dialogue.vn", "com.otus.dialogue.interaction"]`），再用 `Window > General > Test Runner` 跑 EditMode
5. 核心**不引用**扩展程序集：Setup 等工具用 `Type.GetType("类型名, 程序集名")` 探测扩展是否安装，未装自动降级——给核心加「直接 using 扩展类型」的代码会破坏这个约定

## 模块

| 模块 | 说明 |
|---|---|
| 核心 Runtime/Dialogue.asmdef | 数据层（DialogueAsset/Node/SpeakerAsset/UIConfig）+ 运行时（Manager 状态机打字机 / UI 代码自建 + 布局预制体覆盖 + 条目模板克隆 / Validator / 布局契约 / 节点进入命令槽派发） |
| 核心 Editor/Dialogue.Editor.asmdef | 卡片 Inspector、免 Play 预览窗、节点图、样式编辑器、UI 布局导出与校验工具、Setup 工具（扩展感知）、保存校验钩子 |
| 核心 Tests/Editor | EditMode 单元测试（数据跳转/校验规则/Speaker 回退链/样式解析/布局契约与回退） |
| vn 扩展（com.otus.dialogue.vn） | Runtime：DialogueDirector；Editor：Director Manager 窗口；Tests：导演串播测试（后续 BGM/音效/存档 UI 落此包） |
| 交互扩展（com.otus.dialogue.interaction） | Runtime：DialogueTrigger / DialogueDemoTrigger；Editor：Trigger Manager 窗口 |

## 更新已安装的插件

按当初的安装方式操作：

- **git URL 安装**：Package Manager 选中包 → 点 Update 箭头（跟随分支最新）；当初带 `#v某版本` 锁了 tag 的先 Remove 再用新 tag 重装
- **本地 zip 安装**：下载新 zip 解压 → Package Manager → `+` → Install package from disk 重选 `package.json`（同包名高版本原地替换）
- **embedded（拷进工程 Packages/）**：删旧文件夹拷入新版（GUID 不变，引用不断；自己在包内改过的代码先备份）

任何方式升级都**不动用户数据**（Assets/Resources 样式、Assets/Dialogue 对话与角色、布局预制体全在项目侧）。

## 迁移到 2.0（从 1.x）

2.0 拆为核心 + 两个扩展包。对话资产、Speaker、UI 配置、布局预制体全部兼容，只是组件换了住处：

- 升级核心后，场景里原有的 `DialogueTrigger` / `DialogueDemoTrigger` / `DialogueDirector` 会显示 Missing Script——装上 `com.otus.dialogue.interaction`（前两者）或 `com.otus.dialogue.vn`（导演）即恢复，脚本 GUID 未变，序列化引用不断
- 只用核心也完全可用：代码 `DialogueManager.Instance.StartDialogue(asset)` 开播不依赖任何扩展
- 旧菜单 `Dialogue > Trigger Manager` 里的「全局入口」区拆成了独立菜单 `Dialogue > Director Manager`（vn 扩展）
- 新扩展点：对话节点可配「进入命令」（如 `bgm=森林.mp3`），进入该句时经 `DialogueManager.CommandReceived` 事件逐条派发——core 只派发不解释，语义由扩展包或项目脚本订阅定义

## 约定与注意

- **用户数据全部在项目侧**（Assets/Resources 的样式、Assets/Dialogue 的对话与角色、Assets/Fonts 的字体）——升级本包不会覆盖任何用户数据
- Speaker 资产：右键 `Create > Dialogue > Speaker`（显示名/头像/名字颜色覆盖；无头像自动显示名字首字）
- 对话触发（交互扩展的 `Dialogue Trigger` 组件，或触发器管理窗口一键添加）：**靠近+按键**（Trigger Collider 感应玩家 Tag，范围内显示「按 E 交谈」提示，按键可改；缺 Collider 时自动补 Box Collider）/ **自动播放**（场景加载后延迟 N 秒）；「只触发一次」为本次运行标记，不写入存档。点击物体的 Demo 用法仍是 DialogueDemoTrigger
- 视觉小说全局入口用 vn 扩展的 `DialogueDirector`（章节自动串播 + 变量条件分歧 + Finished 事件）；它假定自己是唯一对话驱动，不要与触发器混用。跨章节存档目前需自行记录章节索引（快照为单对话粒度）
- 每对话样式：Dialogue 资产 Inspector 里「创建新样式…」以全局默认为起点
- UI：默认代码搭建零配置；要人为布置用「导出当前 UI 为预制体」（快速开始 6）。布局归预制体、皮肤归配置，缺必需节点运行时自动回退默认布局。配置了 `ChoiceTemplate`/`EntryTemplate` 模板后，选项按钮/历史条目的配色字号归模板（配置里的选项底色/选项文字色只作用于无模板的代码生成观感）
- 本项目开发者注意：本项目 `Assets/Dialogue/` 与 `Samples~/BasicDemo/` 内容同源同 GUID，**不要在本项目重复导入 Samples**（GUID 冲突）
- 更新发布：改代码 → 提升 `package.json` 的 version → 重新分发；脚本 GUID 不变，用户资产引用不断
