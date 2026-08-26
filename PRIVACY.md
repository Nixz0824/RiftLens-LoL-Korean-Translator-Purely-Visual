# RiftLens 隐私说明 / Privacy Notice

[简体中文](#简体中文) · [English](#english)

## 简体中文

### 核心原则

RiftLens 采用数据最小化设计。默认“本地 OCR”模式下，框选区域截图只在本机内存中短暂存在，用于画面变化检测和应用内置的 PaddleOCRv5/ONNX Runtime 识别；必要时会在本机回退到 Tesseract。只有用户主动选择 DeepSeek V4 Vision 实验模式并明确同意后，框选区域裁剪图才会发送给 DeepSeek；不会发送全屏。

### 数据清单

| 数据 | 如何处理 |
| --- | --- |
| 屏幕截图 | 本地 OCR 模式下仅在本机内存处理。用户主动选择并同意 DeepSeek V4 Vision 实验模式后，发生稳定画面变化且本地无法单独完成玩家聊天识别时，会把框选区域 JPEG（原始尺寸）发送给用户配置的 DeepSeek API；纯 HUD 帧留在本地，不会发送全屏。另只有用户主动开启“真实评估记录”时才写入所选本机文件夹 |
| OCR 原文与玩家标签 | 分类后，新内容会在启用联网翻译时发送到用户配置的 AI 地址 |
| 英雄命中与摘要偏好 | 与文本一同发送，用于名称对齐、目标语言和播报策略 |
| AI 生成的播报文字 | 启用在线神经语音时发送给 Microsoft Edge 在线语音服务以合成音频 |
| API Key | 仅保存于 Windows 凭据管理器，目标名为 `RiftLens/TranslationApi` |
| 普通设置 | 保存在 `%LOCALAPPDATA%\RiftLens\settings.json`，不含 API Key |
| 翻译历史 | 默认仅保留在当前进程内存和界面中；用户主动开启“本地保存”后，JSONL 会写入 `%LOCALAPPDATA%\RiftLens\history`。文件可能包含玩家昵称、聊天原文与译文，不含 API Key，也不会自动上传 |
| 脱敏诊断记录 | 默认仅保存在内存；只有用户主动点击“导出诊断”才写入本地 JSON。不含截图、API Key，并用会话别名替换已识别的玩家 ID |
| 真实评估记录 | 默认关闭。用户确认隐私警告并选择文件夹后，保存框选区域原始截图、OCR、分类、实际译文与播报决策；可能包含玩家昵称和聊天，但不含 API Key、全屏截图、游戏内存或网络数据包，也不会自动上传 |

RiftLens 不包含遥测、广告 SDK、用户账号系统、服务端数据库或自动错误上报。网络失败只更新本地状态，不会在对局中反复弹窗。

### 第三方服务

- **翻译服务：** 用户自行选择 OpenAI Chat Completions 兼容服务。DeepSeek 只是默认地址和模型。服务方可能记录文本、IP、计费与请求元数据；请在配置前阅读其条款与隐私政策。
- **实验视觉识别：** 仅在用户选择 `DeepSeek V4 Vision（实验）` 并勾选截图发送同意后，框选区域 JPEG 才可能发送给 DeepSeek 的 `deepseek-v4-flash-vision-exp`。发送前先在本机执行保守的大面积遮挡检测，并且只有仍未解决的玩家聊天才上传；明确被不透明面板覆盖的帧、以及本地已能确定的 HUD/Ping 会直接跳过，不编码、不发起 Vision 请求。通过门控的裁剪图可能包含昵称、聊天和客户端界面，服务商还可能处理 IP、计费和请求元数据。相同裁剪图会同时在本机内存中运行内置 PP-OCRv5，用于行位置、文字颜色、受限字段校验和即时回退；本地辅助结果不会发送给新的第三方。
- **在线语音：** 启用神经语音后，只有 AI 生成的播报文字会发送到 Microsoft Edge 在线语音服务。截图、OCR 原文和 API Key 不会随语音请求发送。中文固定使用晓晓；晓晓暂时不可用时跳过本条并稍后重试，不会自动切换其他中文音色。英文保留本机语音回退。
- **GitHub：** 只用于源码托管和可选下载，不是软件运行依赖。应用不会在后台连接 GitHub，除非未来的更新检查功能明确实现并由用户触发。

玩家昵称和聊天内容可能构成个人信息。录制、直播或分享界面截图前，请自行遮挡昵称、API 配置和聊天内容，并遵守所在地区法律及平台规则。

### 用户控制

- 暂停翻译会停止新的截图、OCR 和联网翻译请求；已经发出的单次请求返回后会被丢弃。
- 用户可随时切回“本地 OCR”或撤回 Vision 同意；之后新的框选截图不再发送给视觉模型。
- 关闭应用会清除当前会话的内存历史；已由用户主动开启本地保存而写入的 JSONL 文件会保留，直到用户自行删除。
- “导出诊断”只在用户选择保存位置后写入脱敏 JSON；分享前仍应自行检查聊天正文是否包含个人信息。
- “真实评估记录”与普通诊断不同：为了人工建立 OCR 标准答案，它会保存框选区域的原始裁剪图。停止翻译、手动停止记录或退出应用时会安全收尾；分享整个文件夹前必须检查并遮挡昵称和聊天内容。
- 删除 `%LOCALAPPDATA%\RiftLens\history` 可移除用户主动保存的本地翻译历史；分享这些文件前应检查并遮挡昵称和聊天内容。
- 删除 `%LOCALAPPDATA%\RiftLens\settings.json` 可移除本地设置。
- 可在 Windows 凭据管理器中删除 `RiftLens/TranslationApi` 以移除 API Key。

本说明描述当前源码版本。Fork 或修改版的维护者可能改变数据行为，用户应以实际发行者提供的说明和源码为准。

## English

### Core principle

RiftLens follows data minimization. In the default Local OCR mode, selected-region screenshots exist briefly in memory for change detection and bundled PaddleOCRv5/ONNX Runtime recognition, with an on-device Tesseract fallback. Only after the user selects DeepSeek V4 Vision experimental mode and explicitly consents may selected-region crops be sent to DeepSeek; the full screen is never sent.

### Data inventory

| Data | Handling |
| --- | --- |
| Screen captures | Processed in local memory in Local OCR mode. After the user selects and consents to DeepSeek V4 Vision experimental mode, a native-size JPEG of the selected crop may be sent to the configured DeepSeek API after a stable visual change when player chat is still unresolved; HUD-only frames stay local and the full screen is never sent. Only an explicitly started Real evaluation capture writes crops to a chosen local folder |
| OCR text and speaker labels | New, classified content is sent to the user-configured AI endpoint when online translation is enabled |
| Champion matches and summary preferences | Sent with text for name alignment, target language, and speech behavior |
| AI-generated spoken text | Sent to Microsoft Edge online speech only when neural speech is enabled |
| API key | Stored only in Windows Credential Manager under `RiftLens/TranslationApi` |
| Ordinary settings | Stored in `%LOCALAPPDATA%\RiftLens\settings.json`, without the API key |
| Translation history | Held in current-process memory and UI by default. If the user explicitly enables Local saving, JSONL is written under `%LOCALAPPDATA%\RiftLens\history`. Files may contain player names, source chat, and translations; they exclude the API key and are never uploaded automatically |
| Redacted diagnostic trace | Held in memory by default and written to local JSON only when the user selects Export diagnostics. It excludes screenshots and API keys and replaces recognized player IDs with session aliases |
| Real evaluation capture | Off by default. After a privacy warning and folder selection, it stores original selected-region crops, OCR, classifications, actual translations, and speech decisions. It may contain player names and chat, but never an API key, full-screen capture, game-memory data, or packets, and is never uploaded automatically |

RiftLens has no telemetry, advertising SDK, account system, backend database, or automatic crash reporting. Network failures update local status without repeatedly interrupting a match.

### Third-party services

- **Translation:** the user selects an OpenAI Chat Completions-compatible provider. DeepSeek is only the default. Providers may process text, IP addresses, billing information, and request metadata under their own policies.
- **Experimental vision recognition:** only after the user selects `DeepSeek V4 Vision (experimental)` and checks screenshot consent may a selected-crop JPEG be sent to DeepSeek's `deepseek-v4-flash-vision-exp`. A conservative local large-obstruction check runs first, and only unresolved player chat is uploaded; a crop clearly covered by an opaque panel, or a frame that local OCR already classified as HUD/Ping, is skipped before encoding and before any Vision request. Accepted crops may contain player names, chat, and client UI, while the provider may also process IP address, billing, and request metadata. The same crop is processed in local memory by bundled PP-OCRv5 for line geometry, text colour, constrained field corroboration, and immediate fallback; that local evidence is not sent to another provider.
- **Online speech:** only the AI-generated spoken summary is sent to Microsoft Edge online speech. Screenshots, OCR source text, and the API key are not included. Chinese is voice-locked to Xiaoxiao and skips an item on a transient failure rather than changing timbre; English retains a local installed-voice fallback.
- **GitHub:** source hosting and optional downloads only. GitHub is not required at runtime. The application does not contact GitHub in the background unless a future, user-triggered update check is explicitly implemented.

Player names and chat may be personal data. Redact names, API configuration, and chat before sharing screenshots, and follow applicable law and platform rules.

### User controls

- Pausing stops new capture, OCR, and translation requests. A response from an already-sent request is discarded.
- The user can switch back to Local OCR or withdraw Vision consent at any time; new selected-region crops are no longer sent to the vision model afterwards.
- Closing the app clears in-memory session history. JSONL files written after the user explicitly enables local saving remain until the user deletes them.
- Export diagnostics writes a redacted JSON file only after the user chooses a save location. Review chat content for personal information before sharing it.
- Real evaluation capture is intentionally different from ordinary diagnostics: it retains original selected-region crops so OCR ground truth can be annotated. It is finalized when capture is stopped, translation is stopped, or the app exits. Review and redact names and chat before sharing the folder.
- Delete `%LOCALAPPDATA%\RiftLens\history` to remove locally saved translation history. Review and redact names and chat before sharing these files.
- Delete `%LOCALAPPDATA%\RiftLens\settings.json` to remove local settings.
- Delete `RiftLens/TranslationApi` in Windows Credential Manager to remove the API key.

This notice describes the current source version. Forks may change data behavior; rely on the actual distributor's notice and source code.
