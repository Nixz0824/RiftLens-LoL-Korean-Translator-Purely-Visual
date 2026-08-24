<div align="center">
  <img src="assets/readme/hero.svg" width="100%" alt="RiftLens — LoL Korean Server Chat Screen OCR Translation & Natural Voice Narration | LOL 韩服聊天屏幕 OCR 翻译与自然播报">
</div>

<div align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a>
  <br><br>
  <img alt="Platform Windows" src="https://img.shields.io/badge/Windows-10%2F11-082942?style=flat-square">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-7b72ff?style=flat-square">
  <img alt="License MIT" src="https://img.shields.io/badge/License-MIT-08b982?style=flat-square">
  <img alt="Version 2.2.0" src="https://img.shields.io/badge/Version-2.2.0-7b72ff?style=flat-square">
  <img alt="Status MVP" src="https://img.shields.io/badge/Status-MVP-f0a24b?style=flat-square">
</div>

> [!IMPORTANT]
> **RiftLens is a personal, runnable MVP built with a vibe-coding workflow — not an official Riot Games product.** Please read the [Game Compatibility & Disclaimer](#game-compatibility--disclaimer--游戏兼容性与免责声明) section before use. A low-intrusion design does not equal a zero-risk guarantee.
>
> RiftLens 是作者个人以 vibe coding 方式开发的可运行 MVP，并非 Riot Games 官方产品。使用前请阅读[游戏兼容性与免责声明](#游戏兼容性与免责声明)；低侵入设计不等于零风险承诺。

## RiftLens 2.2 · What Changed / 本次更新

[Download RiftLens 2.2 for Windows x64](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.2.0) · [下载 Windows x64 版 RiftLens 2.2](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.2.0)

- **A new single-page light workspace. · 全新的浅色单页工作区。** API connection, voice controls, system information, translation history, and quick actions now share one focused desktop window based on the maintainer's Figma layout.<br>API 连接、语音、系统信息、翻译记录和快捷操作整合进一个正式桌面界面，整体依据维护者的 Figma 设计落地。
- **Welcome first, guide once. · 每次欢迎，首次引导。** Every launch opens the product welcome screen; first use adds a read-only three-step introduction before entering the home workspace.<br>每次启动先展示产品欢迎页；第一次使用会额外展示只读三步说明，之后进入主页。
- **One visual layer, native corners. · 单层界面与系统圆角。** The WebUI fills the window directly and Windows DWM owns the outer corners, removing the nested canvas and jagged double rounding.<br>WebUI 直接铺满窗口，外框圆角交给 Windows DWM，移除内外双层画布和锯齿叠加。
- **Bilingual layout that follows content. · 中英文内容驱动布局。** Cards grow with real copy instead of language-specific fixed heights, reducing English crowding and Chinese empty card space.<br>卡片按真实文字自然增高，不再为中英文分别硬编码行高，减少英文拥挤和中文卡内空白。
- **Eight palettes plus custom colour. · 八套配色与自定义颜色。** The full interface and native floating control share the chosen light palette; settings close naturally when the user clicks elsewhere.<br>主界面与原生悬浮控制同步采用所选浅色配色；点击界面其他位置即可收起设置菜单。
- **A quieter in-match control. · 更克制的局内控制。** The floating control now keeps only pause/resume, volume, and drag, with cleaner alignment and Alpha-safe text rendering.<br>悬浮控制只保留暂停/继续、音量与拖动，并修正对齐和透明窗口文字渲染。
- **Optional local history. · 可选本地历史。** Translation history remains session-only by default; users may explicitly enable local JSONL saving from the history card.<br>翻译记录默认仍仅限本次会话；用户可在记录卡片中主动开启本地 JSONL 保存。
- **Close-to-tray behaves as labelled. · 关闭到托盘按设置生效。** With the switch enabled, closing hides the main window while the process and tray entry remain alive.<br>开启后点击关闭只隐藏主窗口，后台进程和托盘入口继续运行。

> [!NOTE]
> 2.2 is still a personal, vibe-coded early MVP. This release focuses on desktop usability and presentation; OCR accuracy, future LoL client wording, unusual HUD layouts, and other edge cases still need continued testing.
>
> 2.2 仍是个人以 vibe coding 方式维护的早期 MVP。本次重点是桌面交互与视觉呈现；OCR 准确率、LOL 客户端后续文案、少见 HUD 布局和其他边界情况仍需持续测试。

## At a Glance · 一眼看懂

When playing on the LOL Korean server or watching Korean livestreams, even with the official language settings applied, messages from local players remain in Korean and are impossible to understand. I built this tool to solve that problem.

**RiftLens is a Windows screen text translation assistant built for LOL Korean server game chat, champion-select chat, and livestream footage.** You select a screen region; the tool captures, detects changes, and performs Korean, Chinese, and English OCR locally, then hands the filtered text to your own AI service for translation — presenting the result as a low-distraction subtitle or natural voice narration.

游玩LOL韩服或者观看直播的时候，即使有设置官方语言翻译，也会因为本土玩家消息采用韩文而无法理解。针对这个问题，我制作了这个软件。

RiftLens 是一款面向 **LOL 韩服游戏聊天、英雄选择聊天和直播画面**的 Windows 屏幕文字翻译助手。框选一块屏幕区域后，它在本地完成截图、变化检测与韩中英 OCR，再把筛选后的文字交给用户自行配置的 AI 服务翻译，并以低干扰字幕或自然语音呈现。

**It does not read game memory, inject into the game, simulate keyboard/mouse input, send messages on your behalf, or bundle any API key.**

它不读取游戏内存、不注入游戏、不模拟键鼠、不替用户发送消息，也不捆绑任何 API Key。

<div align="center">
  <img src="assets/readme/home.png" width="92%" alt="RiftLens Chinese main interface | RiftLens 中文主界面">
</div>

## Use Cases · 适合这些场景

| Scenario · 场景 | What RiftLens does · RiftLens 做什么 |
| --- | --- |
| **LOL KR ranked / normal games**<br>LOL 韩服对局 | Recognises player chat, filters most system events, and translates or narrates what matters.<br>识别玩家发言，过滤大部分系统事件，并将重要内容翻译或播报。 |
| **Champion select**<br>英雄选择阶段 | Freely select the champion-select chat area; no reliance on fixed HUD coordinates.<br>自由框选选择英雄聊天区，不依赖固定 HUD 坐标。 |
| **Watching KR livestreams**<br>观看韩服直播 | Select the chat or subtitle region in a livestream window — works as a general Korean screen translator.<br>框选直播画面中的聊天或字幕区域，作为通用韩文屏幕翻译器使用。 |
| **OCR & contextual translation research**<br>OCR 与语境翻译研究 | Provides a compact reference implementation for WinForms, WebView2, PaddleOCR/ONNX Runtime, and OpenAI-compatible translation.<br>提供一个体量克制的 WinForms、WebView2、PaddleOCR/ONNX Runtime 与 OpenAI-compatible 参考实现。 |

## Core Features · 核心特点

- **Eyes on the screen only. · 只看屏幕。** Freely select an OCR recognition region; optionally select a separate overlay display region. The main window auto-hides during selection and restores afterwards.<br>自由框选识别区，也可单独框选译文显示区；框选时主界面自动隐藏，完成后恢复。
- **OCR works out of the box. · OCR 开箱即用。** A shared PP-OCRv5 Mobile detector feeds Korean-English and Chinese-English recognizers; word-level confidence and Unicode-script fusion preserve all three languages in one line. The previous Tesseract engine remains a local fallback.<br>内置 PP-OCRv5 Mobile 共享检测器、韩英与中英识别器；通过词状片段置信度和 Unicode 脚本融合保留同一行中的三种语言，并保留原 Tesseract 作为本地兜底。
- **Optimised for scrolling chat. · 为滚动聊天优化。** Low-frequency small-region capture, pixel-change detection, stable-frame waiting, clipped-row filtering, timestamp stripping, and scroll deduplication work together.<br>小区域低频采样、画面变化检测、稳定帧识别、残行过滤、时间戳清理与上滚去重协同工作。
- **Classify first, then translate. · 先分类，再翻译。** Uses the player ID + `(champion):` chat structure first and LoL text colours only as supporting evidence. Tactical pings, kill events, neutral objectives, client reminders, and other system notices have separate, off-by-default switches; player chat remains first priority.<br>优先依据“玩家 ID +（英雄名）+ 冒号”的聊天结构，LOL 文字颜色只作辅助证据。战术信号、击杀播报、中立资源、客户端提醒与其他系统通知均可独立开关且默认关闭；玩家聊天始终优先。
- **Evidence-checked turn narration. · 有证据校验的轮次播报。** Consecutive messages from the same player are aggregated, but a summary may be spoken only when its evidence, numbers, negation, and action claims can be checked against the source text. Otherwise RiftLens keeps the literal overlay and stays silent.<br>同一玩家连续发言会聚合，但只有证据、数字、否定和行动描述都能对照原文时才允许播报；否则保留逐条直译字幕并静音。
- **Civilised, but keeps the emotion. · 文明化但不抹掉情绪。** Normal communication keeps its original meaning; hostile content has specific attacks removed while retaining available tactical information and approximate emotional tone.<br>正常沟通保留原意；不友善内容删除具体攻击措辞，仍尽量保留其中可用的战术信息和大致情绪。
- **Champion names are never guessed. · 英雄名称不猜测。** Built-in 173-champion Chinese–Korean–English name mapping from Data Dragon 16.14.1; a champion name is only used when the text actually contains it — otherwise "teammate" is used.<br>内置 Data Dragon 16.14.1 的 173 名英雄中、韩、英名称映射；只有文字中实际命中英雄名时才使用角色名，否则称"队友"。
- **Natural voice with graceful fallback. · 自然语音与降级。** Chinese defaults to `zh-CN-XiaoxiaoNeural`; English uses `en-US-JennyNeural`. Falls back silently to Windows local TTS when the online voice is unavailable.<br>中文默认尝试 `zh-CN-XiaoxiaoNeural`，英文使用 `en-US-JennyNeural`；在线语音不可用时安静地回退到 Windows 本地语音。
- **Never steals game focus. · 不抢游戏焦点。** The translation overlay is non-activating, mouse-transparent, and excluded from screen capture. A compact floating control keeps pause/resume, volume, and drag close at hand.<br>译文层不激活、鼠标穿透并排除自身截图；紧凑悬浮控件只保留暂停/继续、音量与拖动。
- **Bilingual Chinese–English. · 中英双语。** UI, AI output, tactical signals, history column headers, and voice language switch together, with automated bilingual interaction checks.<br>界面、AI 输出、战术信号、历史列名和语音语言统一切换，并有双语交互自动化检查。

## How Data Flows · 数据如何流动

<div align="center">
  <img src="assets/readme/workflow.svg" width="100%" alt="RiftLens data flow: screenshots and OCR stay local; only text is sent to user-configured services | RiftLens 数据流：截图和 OCR 留在本地，只有文本发送给用户配置的服务">
</div>

| Data · 数据 | Where it goes · 去向 | Persisted? · 是否持久化 |
| --- | --- | --- |
| **Screenshots of the selected region**<br>框选区域截图 | In-memory change detection and local PaddleOCR/ONNX Runtime inference by default. An explicitly started real-evaluation capture can write only these selected-region crops to a user-chosen local folder.<br>默认仅在本机内存中进行变化检测、PaddleOCR/ONNX Runtime 识别及 Tesseract 兜底；只有用户主动开启真实评估记录时，才会把框选区域裁剪图写入所选本机文件夹。 | No by default; yes only after explicit opt-in · 默认否；仅主动开启后保存 |
| **New OCR text, speaker labels, hero hits & summary preferences**<br>OCR 后的新文字、发言人标签、英雄命中与摘要偏好 | Your configured OpenAI-compatible translation endpoint.<br>用户配置的 OpenAI-compatible 翻译接口。 | Governed by that service's policy.<br>由该服务的政策决定。 |
| **AI-generated spoken narration text**<br>AI 生成的播报文字 | Microsoft Edge online speech service (when online neural voice is enabled).<br>启用在线神经语音时发送给 Microsoft Edge 在线语音服务。 | RiftLens does not save it.<br>RiftLens 不保存。 |
| **API Key**<br>API Key | Windows Credential Manager, target name `RiftLens/TranslationApi`.<br>Windows 凭据管理器，目标名 `RiftLens/TranslationApi`。 | Yes, protected by the current Windows user.<br>是，由当前 Windows 用户保护。 |
| **Other settings & region coordinates**<br>其他设置和区域坐标 | `%LOCALAPPDATA%\RiftLens\settings.json` — excludes the API Key.<br>`%LOCALAPPDATA%\RiftLens\settings.json`，不含 API Key。 | Yes · 是 |
| **Translation history**<br>翻译历史 | In memory and on screen by default. If the user enables local saving, JSONL is written under the current user's RiftLens local-data directory.<br>默认仅在内存与界面中；用户主动开启本地保存后，JSONL 会写入当前用户的 RiftLens 本地数据目录。 | No by default; yes only after explicit opt-in.<br>默认否；仅主动开启后保存。 |
| **Redacted diagnostics / real-evaluation records**<br>脱敏诊断 / 真实评估记录 | Written only after the user explicitly exports diagnostics or starts a local evaluation capture; never uploaded automatically.<br>只有用户主动导出诊断或开启本地评估记录时才写入；不会自动上传。 | User-selected local file/folder · 用户选择的本机文件或文件夹 |

Full details in [PRIVACY.md](PRIVACY.md). Using online translation or online speech means relevant text is subject to the corresponding third-party terms of service and privacy policy.

完整说明见 [PRIVACY.md](PRIVACY.md)。使用联网翻译或在线语音即表示相关文本会受对应第三方服务条款和隐私政策约束。

## Quick Start · 快速开始

### Requirements · 运行环境

- **Windows 10 22H2 or Windows 11, x64.**<br>Windows 10 22H2 或 Windows 11，x64。
- **Microsoft Edge WebView2 Runtime** — typically already present on current Windows / Edge.<br>Microsoft Edge WebView2 Runtime；当前 Windows/Edge 通常已包含。
- **Smaller publish output** requires .NET 8 Desktop Runtime; the self-contained publish output does **not** need a preinstalled .NET.<br>小体积发布包需要 .NET 8 Desktop Runtime；自包含发布包不需要预装 .NET。
- **Borderless window mode is recommended** for games. Exclusive fullscreen does not guarantee third-party overlays can display.<br>游戏推荐无边框窗口模式。独占全屏不保证能显示第三方悬浮层。

### Download 2.2 · 下载 2.2

Open the [v2.2.0 release page](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.2.0), download `RiftLens-2.2.0-win-x64.zip`, extract the complete folder, and run `RiftLens.exe`. Keep the packaged `WebUI` folder beside the executable.<br>打开 [v2.2.0 Release 页面](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.2.0)，下载并完整解压 `RiftLens-2.2.0-win-x64.zip`，再运行 `RiftLens.exe`；请保留程序旁边随包提供的 `WebUI` 文件夹。

### Build from Source · 从源码运行

```powershell
git clone https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual.git RiftLens
cd RiftLens
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./run.ps1
```

`build.ps1` builds the main program and the self-test project, then runs local logic checks. It does **not** request or read your translation API Key.

`build.ps1` 会构建主程序与自测项目，并执行本地逻辑自检。它不会要求或读取你的翻译 API Key。

### First-Time Use · 第一次使用

1. **Select a recognition region** on the home page — frame only the chat or text you want translated.<br>在首页选择 **框选翻译识别区**，只圈住聊天或需要翻译的文字。
2. **Wait for the built-in OCR** to show ready.<br>等待内置 OCR 显示就绪。
3. **Configure an OpenAI Chat Completions-compatible endpoint** reachable from your network, along with your model name and API Key.<br>配置可从中国大陆网络访问的 OpenAI Chat Completions 兼容地址、模型与自己的 API Key。
4. **Click Start Translation** and return to your game. The first scan only establishes a screen-text baseline — it will not suddenly narrate old messages.<br>点击 **开始翻译** 后返回游戏；首次扫描只建立屏幕历史基线，不会突然播报旧消息。
5. **Check the translation-history card** to compare "OCR Raw" against the translation, and verify your HUD scale, chat font size and capture region are appropriate.<br>在主页翻译记录卡片中对照“OCR 识别原文”和译文，检查 HUD 缩放、聊天字号与框选范围是否合适。

> [!NOTE]
> **RiftLens uses a BYOK (Bring Your Own Key) model.** The project does not provide, share, or pre-configure API keys from DeepSeek, OpenAI, or any other service. DeepSeek is only the default example — both the compatible endpoint and model are replaceable.
>
> RiftLens 采用 BYOK（Bring Your Own Key）。项目不会提供、共享或预置 DeepSeek/OpenAI 等服务的 Key。DeepSeek 只是默认配置，兼容端点和模型均可更换。

## Interface · 界面

The 2.2 workspace keeps API connection, voice controls, system information, and translation history visible together. The top-right settings popover contains palette, runtime, speech, system-message, and overlay options; clicking elsewhere closes it naturally. In-game translation and floating controls remain lightweight native windows.<br>2.2 工作区同时展示 API 连接、语音控制、系统信息和翻译记录。右上角设置弹窗集中放置配色、运行、播报、系统消息和悬浮显示选项，点击其他位置即可自然收起；局内译文与悬浮控制仍使用原生轻量窗口。

## Architecture · 架构

```text
RiftLens/
├─ Models/       Settings, regions, translation request & result models
├─ Services/     Capture, preprocessing, OCR, dedup, classification, translation & speech
├─ UI/           WinForms main window, region selector, translation overlay & floating control
├─ WebUI/        Locally-bundled HTML / CSS / JavaScript interface
└─ Assets/       Hero dictionary and Korean–Chinese–English OCR models

RiftLens.SelfTest/
├─ Program.cs                    Core logic & boundary regression tests
└─ webui-interaction-test.js     Click-interaction tests for Chinese & English interfaces
```

```text
RiftLens/
├─ Models/       设置、区域、翻译请求与结果模型
├─ Services/     截图、预处理、OCR、去重、分类、翻译与语音
├─ UI/           WinForms 主窗口、框选器、译文层与悬浮控制
├─ WebUI/        随程序本地打包的 HTML / CSS / JavaScript 界面
└─ Assets/       英雄词典和韩中英 OCR 模型

RiftLens.SelfTest/
├─ Program.cs                    核心逻辑与边界回归测试
└─ webui-interaction-test.js     中英文界面的实际点击交互测试
```

Detailed component responsibilities, thread boundaries, and extension points are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The project is deliberately kept as two .NET projects plus a small set of local resources — no server, database, or heavy front-end build chain is introduced.

详细组件职责、线程边界和扩展点见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。项目刻意保持为两个 .NET 项目和少量本地资源，不引入服务端、数据库或大型前端构建链。

## Build, Test & Publish · 构建、测试与发布目录

```powershell
# Build & run core self-tests · 构建并执行核心自检
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1

# Run bilingual WebUI click tests separately (requires Microsoft Edge)
# 单独运行双语 WebUI 点击测试（需要 Microsoft Edge）
node ./RiftLens.SelfTest/webui-interaction-test.js

# Small output — depends on the target machine having .NET 8 Desktop Runtime
# 小体积、依赖目标电脑的 .NET 8 Desktop Runtime
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1

# Larger output — self-contained, no preinstalled .NET required
# 体积较大、不要求目标电脑预装 .NET
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1 -SelfContained
```

Publish output goes only into the Git-ignored `artifacts/` directory. CI builds on Windows, runs core tests, checks the bilingual interface, and produces downloadable build artefacts without automatic release publishing.

发布结果只写入被 Git 忽略的 `artifacts/`。CI 在 Windows 上构建、运行核心测试、检查双语界面，并生成不自动发布的可下载构建产物。

## Current Limitations · 当前限制

- **OCR accuracy** is still affected by chat font size, HUD scaling, dynamic backgrounds, font outlines, compressed video quality, and capture region boundaries; no specific accuracy rate is promised yet.<br>OCR 精度仍受聊天字号、HUD 缩放、动态背景、字体描边、压缩画质和框选范围影响；尚未承诺特定准确率。
- **The classifier** prioritises avoiding misreporting system notifications as player chat, but new client versions or custom localisation text may still produce unknown formats.<br>分类器优先减少"把系统通知当玩家发言"的误播，但新版本客户端或特殊汉化文本仍可能出现未知格式。
- **Capture regions** follow the League client as window-relative ratios when the client can be identified; absolute-screen fallback regions may still need re-selection after display or resolution changes.<br>能够识别到 LOL 客户端时，框选区域按窗口比例跟随；使用屏幕绝对坐标兜底时，切换显示器或分辨率后仍可能需要重新框选。
- **Online AI and neural speech** both depend on your network environment and third-party service availability.<br>在线 AI 与神经语音都依赖用户所在网络环境和第三方服务可用性。
- **This remains a personal early MVP.** The 2.2 package passes the included automated checks, but users should still scan downloads with current antivirus software and expect edge cases in real matches.<br>这仍是个人维护的早期 MVP。2.2 发布包已通过项目内自动化检查，但用户仍应使用最新杀毒软件扫描下载内容，并预期真实对局中仍有边界情况。

## Game Compatibility & Disclaimer · 游戏兼容性与免责声明

**RiftLens only processes screen pixels.** It does not read or modify game processes, does not inject, does not intercept network traffic, does not automate any action, and will not send messages on behalf of the player. These boundaries are meant to reduce performance and anti-cheat risks — **but they do not represent endorsement by Riot Games, nor do they constitute a guarantee of compatibility, account safety, or zero ban risk.** Game and anti-cheat rules may change; users should verify local regulations, Riot Games' current terms, and their platform's rules themselves.

RiftLens 只处理屏幕像素，不读取或修改游戏进程，不注入、不抓包、不自动操作，也不会代替玩家发送内容。这些边界用于降低性能与反作弊风险，但**不代表 Riot Games 的认可，也不构成兼容性、账号安全或零封号风险保证**。游戏和反作弊规则可能变化，用户应自行确认当地法规、Riot Games 当前条款与所在平台规则。

RiftLens isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

**League of Legends, champion names, Data Dragon data, and related visual and textual assets** are the property of Riot Games or the respective rights holders. This repository does not contain game client assets; the champion name mapping is used only where necessary for interoperability and contextual translation.

League of Legends、英雄名称、Data Dragon 数据及相关视觉与文字资产的权利归 Riot Games 或相应权利人所有。本仓库不包含游戏客户端资产；英雄名称映射仅用于必要的互操作和语境翻译。

## Copyright & Third-Party Components · 版权与第三方组件

**RiftLens's own source code** is open-sourced under the [MIT License](LICENSE), Copyright © 2026 Nixz. Third-party components and data remain governed by their original licences and are not altered by this project's MIT licence — including PaddleOCR models, Microsoft ONNX Runtime, Tesseract, tessdata_fast, the Tesseract .NET wrapper, Leptonica, NAudio, Microsoft WebView2, and Riot Data Dragon. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.

RiftLens 自有源代码以 [MIT License](LICENSE) 开源，Copyright © 2026 Nixz。第三方组件及数据不因本项目 MIT 许可而改变原许可证，包括 PaddleOCR 模型、Microsoft ONNX Runtime、Tesseract、tessdata_fast、Tesseract .NET wrapper、Leptonica、NAudio、Microsoft WebView2 与 Riot Data Dragon。详情见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

When online speech is provided by Microsoft services, it is additionally subject to Microsoft's terms of service and availability constraints; this project does not own or resell the corresponding voices.

在线语音由 Microsoft 服务提供时，还受 Microsoft 的服务条款与可用性约束；本项目不拥有或转售对应音色。

## Contributing & Security · 参与与安全

- Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code.<br>提交代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。
- For vulnerabilities or sensitive issues, report privately as described in [SECURITY.md](SECURITY.md). Do not paste keys, chat logs, or local paths in public Issues.<br>漏洞与敏感问题请按 [SECURITY.md](SECURITY.md) 私下报告，不要在公开 Issue 粘贴 Key、聊天记录或本机路径。
- See [CHANGELOG.md](CHANGELOG.md) for version changes.<br>版本变化见 [CHANGELOG.md](CHANGELOG.md)。

**Solo developer: Nixz** (GitHub [@Nixz0824](https://github.com/Nixz0824)). Issues and Pull Requests are welcome — just please keep the project's "small and clear" ethos.

个人开发者：**Nixz**（GitHub [@Nixz0824](https://github.com/Nixz0824)）。欢迎以 Issue 或 Pull Request 交流，但请保持项目"小而清楚"的定位。
