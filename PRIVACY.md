# RiftLens 隐私说明 / Privacy Notice

[简体中文](#简体中文) · [English](#english)

## 简体中文

### 核心原则

RiftLens 采用数据最小化设计。它不是云端 OCR 客户端：框选区域的屏幕截图只在本机内存中短暂存在，用于画面变化检测和应用内置的 Tesseract OCR，默认不会被保存或发送到服务器。

### 数据清单

| 数据 | 如何处理 |
| --- | --- |
| 屏幕截图 | 仅本机内存处理；RiftLens 不主动保存或上传 |
| OCR 原文与玩家标签 | 分类后，新内容会在启用联网翻译时发送到用户配置的 AI 地址 |
| 英雄命中与摘要偏好 | 与文本一同发送，用于名称对齐、目标语言和播报策略 |
| AI 生成的播报文字 | 启用在线神经语音时发送给 Microsoft Edge 在线语音服务以合成音频 |
| API Key | 仅保存于 Windows 凭据管理器，目标名为 `RiftLens/TranslationApi` |
| 普通设置 | 保存在 `%LOCALAPPDATA%\RiftLens\settings.json`，不含 API Key |
| 翻译历史 | 仅保留在当前进程内存和界面中；关闭软件后清除 |

RiftLens 不包含遥测、广告 SDK、用户账号系统、服务端数据库或自动错误上报。网络失败只更新本地状态，不会在对局中反复弹窗。

### 第三方服务

- **翻译服务：** 用户自行选择 OpenAI Chat Completions 兼容服务。DeepSeek 只是默认地址和模型。服务方可能记录文本、IP、计费与请求元数据；请在配置前阅读其条款与隐私政策。
- **在线语音：** 启用神经语音后，只有 AI 生成的播报文字会发送到 Microsoft Edge 在线语音服务。截图、OCR 原文和 API Key 不会随语音请求发送。在线语音不可用时应用回退到本机语音。
- **GitHub：** 只用于源码托管和可选下载，不是软件运行依赖。应用不会在后台连接 GitHub，除非未来的更新检查功能明确实现并由用户触发。

玩家昵称和聊天内容可能构成个人信息。录制、直播或分享界面截图前，请自行遮挡昵称、API 配置和聊天内容，并遵守所在地区法律及平台规则。

### 用户控制

- 暂停翻译会停止新的截图、OCR 和联网翻译请求；已经发出的单次请求返回后会被丢弃。
- 关闭应用会清除当前会话历史。
- 删除 `%LOCALAPPDATA%\RiftLens\settings.json` 可移除本地设置。
- 可在 Windows 凭据管理器中删除 `RiftLens/TranslationApi` 以移除 API Key。

本说明描述当前源码版本。Fork 或修改版的维护者可能改变数据行为，用户应以实际发行者提供的说明和源码为准。

## English

### Core principle

RiftLens follows data minimization. It is not a cloud OCR client: screenshots of the selected region exist briefly in local memory for change detection and bundled Tesseract OCR. RiftLens does not save or upload them by default.

### Data inventory

| Data | Handling |
| --- | --- |
| Screen captures | Processed in local memory only; not intentionally saved or uploaded |
| OCR text and speaker labels | New, classified content is sent to the user-configured AI endpoint when online translation is enabled |
| Champion matches and summary preferences | Sent with text for name alignment, target language, and speech behavior |
| AI-generated spoken text | Sent to Microsoft Edge online speech only when neural speech is enabled |
| API key | Stored only in Windows Credential Manager under `RiftLens/TranslationApi` |
| Ordinary settings | Stored in `%LOCALAPPDATA%\RiftLens\settings.json`, without the API key |
| Translation history | Held in current-process memory and UI only; cleared when the app exits |

RiftLens has no telemetry, advertising SDK, account system, backend database, or automatic crash reporting. Network failures update local status without repeatedly interrupting a match.

### Third-party services

- **Translation:** the user selects an OpenAI Chat Completions-compatible provider. DeepSeek is only the default. Providers may process text, IP addresses, billing information, and request metadata under their own policies.
- **Online speech:** only the AI-generated spoken summary is sent to Microsoft Edge online speech. Screenshots, OCR source text, and the API key are not included. RiftLens falls back to a local installed voice when online synthesis fails.
- **GitHub:** source hosting and optional downloads only. GitHub is not required at runtime. The application does not contact GitHub in the background unless a future, user-triggered update check is explicitly implemented.

Player names and chat may be personal data. Redact names, API configuration, and chat before sharing screenshots, and follow applicable law and platform rules.

### User controls

- Pausing stops new capture, OCR, and translation requests. A response from an already-sent request is discarded.
- Closing the app clears session history.
- Delete `%LOCALAPPDATA%\RiftLens\settings.json` to remove local settings.
- Delete `RiftLens/TranslationApi` in Windows Credential Manager to remove the API key.

This notice describes the current source version. Forks may change data behavior; rely on the actual distributor's notice and source code.
