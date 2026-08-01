<div align="center">
  <img src="assets/readme/hero.svg" width="100%" alt="RiftLens — screen OCR translation and natural speech for Korean League chat">
</div>

<div align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a>
  <br><br>
  <img alt="Platform Windows" src="https://img.shields.io/badge/Windows-10%2F11-082942?style=flat-square">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-7b72ff?style=flat-square">
  <img alt="License MIT" src="https://img.shields.io/badge/License-MIT-08b982?style=flat-square">
  <img alt="Version 2.0.0" src="https://img.shields.io/badge/Version-2.0.0-7b72ff?style=flat-square">
  <img alt="Status MVP" src="https://img.shields.io/badge/Status-MVP-f0a24b?style=flat-square">
</div>

> [!IMPORTANT]
> RiftLens is a working personal MVP, not an official Riot Games product. Read the [compatibility disclaimer](#game-compatibility-and-disclaimer) before use; a low-impact design is not a zero-risk promise.

## RiftLens 2.0

[Download RiftLens 2.0 for Windows x64](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.0.0)

- **More capable trilingual OCR.** Bundled PP-OCRv5 Mobile Korean-English and Chinese-English recognizers process the same selected region and fuse word results by confidence and script. Tesseract remains as an on-device fallback.
- **Stable player–champion pairing.** Repeated official champion-name matches are learned per player for the current match, reducing later errors caused by changing chat backgrounds.
- **Evidence-checked narration.** A lightweight local semantic layer separates markers, status shares, cooldowns, requests, plans, and actual actions. `Flash - 20s` means “Flash has 20 seconds remaining,” and an objective ping remains a marker unless its text explicitly describes an ongoing action.
- **Conservative fallback.** AI narration must return an exact source-text evidence fragment and preserve numbers, negation, targets, and action modality. Unsupported speech is silenced while the one-to-one literal translation remains visible.
- **Clearer player-first output.** Player chat is translated by default; tactical pings and system categories remain independently optional. Overlay lines are grouped by speaker, show literal translations for seven seconds, and use refreshed floating-window and Windows icon rendering.

### 2026-08-02 reliability update

- **Timestamp-first parsing.** Game time is treated as `minutes:seconds`, including matches beyond 24 minutes, full-width punctuation, no-space forms, and limited OCR damage. The timestamp colon is resolved before any player/content separator.
- **Deterministic HUD routing.** Common ability, summoner-spell, health/mana, item-gold, ward, objective-timer, on-my-way, danger, assist, missing-enemy, and vision messages are handled locally before free-form AI. A bare `champion + R/Q/W/E` remains a marker instead of becoming an invented cooldown/action claim.
- **Source-aware speech.** Player-authored chat, player-initiated signals, unattributed game signals, and fixed system notices keep separate source labels. Only player-authored chat may use a “player says” framing, and the final speech gate rechecks the latest category switches.
- **Lower retained OCR memory.** ONNX Runtime keeps CPU arena reuse while avoiding a fixed memory pattern for variable chat crops.

#### Same-session replay snapshot

| Metric | Before | After | Relative result |
| --- | ---: | ---: | ---: |
| Timestamp-prefix recall | 3.95% | ~99.2% | +~95.3 percentage points |
| Routing agreement on 141 manually reviewed lines | 36.88% | 141/141 | +63.12 percentage points |
| Observed bad-speech cases covered by the new guards | 0/24 | 24/24 | All observed cases covered |
| OCR process working set | 250.28 MB | 183.00 MB | ~27% lower |
| OCR process private memory | 249.67 MB | 165.17 MB | ~34% lower |
| OCR latency P50 | 1064.16 ms | 1051.06 ms | ~1.2% lower; no measured slowdown |

These figures come from replaying the same single real-use session and a same-machine screenshot probe. The 141 labels were manually reviewed; raw captures, chat logs, annotations, replay files, local paths, and FPS/frametime data are deliberately not published. They describe regression progress on this known session, not future-match accuracy, and this pass did not primarily change OCR recognition quality.

> [!NOTE]
> 2.0 remains an early personal MVP. Future client wording, custom localisation, unusual HUD layouts, and OCR noise may still produce unknown cases. Redacted examples are welcome in Issues.

## What it is

RiftLens is a Windows screen translator for **Korean League of Legends matches, champion-select chat, and livestreams**. Select a screen region and it performs capture, change detection, and Korean, Chinese, and English OCR locally. Only classified text is passed to an AI endpoint configured by the user, then shown as a low-distraction overlay or spoken naturally.

It does not read game memory, inject into the game, simulate keyboard or mouse input, send chat messages, or bundle an API key.

<div align="center">
  <img src="assets/readme/home.png" width="92%" alt="RiftLens main window">
</div>

## Where it helps

| Scenario | What RiftLens does |
| --- | --- |
| Korean-server matches | Recognizes player chat, filters most system events, and translates or speaks important content |
| Champion select | Lets the user select the chat area instead of assuming a fixed HUD position |
| Korean livestreams | Works as a flexible Korean on-screen text translator for stream chat or captions |
| OCR/translation experiments | Provides a compact WinForms, WebView2, PaddleOCR/ONNX Runtime, and OpenAI-compatible reference implementation |

## Highlights

- **Screen-only input.** Select the recognition region and, optionally, a separate translation display region. The main window hides while selecting and returns afterwards.
- **Bundled trilingual OCR.** A shared PP-OCRv5 Mobile detector feeds Korean-English and Chinese-English recognizers. Word-level confidence and Unicode-script fusion preserve all three languages in one line; Tesseract remains an on-device fallback.
- **Rolling-chat handling.** Small-region sampling, change detection, stabilization, partial-line rejection, timestamp cleanup, and scroll deduplication work together.
- **Classify before translating.** The player ID + `(champion):` structure is primary and LoL text colours are supporting evidence only. Tactical pings, kill events, neutral objectives, client reminders, and other system notices have independent, off-by-default switches. Player chat always has priority.
- **Evidence-checked speaker turns.** Consecutive lines from one player are grouped, but a summary may be spoken only when its evidence, numbers, negation, and action claims can be checked against the source. Otherwise the literal overlay remains visible and RiftLens stays silent.
- **Safer speech without erasing emotion.** Normal messages retain meaning. Hostile wording is removed while useful tactics and general emotional tone are preserved where possible.
- **No guessed champions.** A 173-champion Korean, Chinese, and English dictionary based on Data Dragon 16.14.1 is used only when a champion name is actually present.
- **Natural speech with fallback.** Chinese uses `zh-CN-XiaoxiaoNeural`; English uses `en-US-JennyNeural`. RiftLens falls back quietly to an installed Windows voice when online synthesis is unavailable.
- **Game-friendly presentation.** The overlay does not activate, is click-through, and is excluded from its own captures. A compact floating control handles pause, resume, volume, and mute.
- **Chinese and English UI.** The interface, AI output, tactical signals, history labels, and voice language switch together and are covered by interaction tests.

## Data flow

<div align="center">
  <img src="assets/readme/workflow-en.svg" width="100%" alt="RiftLens data flow: screenshots and OCR stay local; only text crosses the network boundary">
</div>

| Data | Destination | Persisted by RiftLens |
| --- | --- | --- |
| Selected-region screenshots | In-memory change detection and local PaddleOCR/ONNX Runtime inference by default. An explicitly started real-evaluation capture can write only these selected-region crops to a user-chosen local folder | No by default; yes only after explicit opt-in |
| New OCR text, speaker label, champion matches, and summary preferences | User-configured OpenAI-compatible translation endpoint | No |
| AI-generated spoken summary | Microsoft Edge online speech when neural speech is enabled | No |
| API key | Windows Credential Manager under `RiftLens/TranslationApi` | Yes, protected for the current Windows user |
| Other settings and coordinates | `%LOCALAPPDATA%\RiftLens\settings.json`, without the key | Yes |
| Translation history | Current process memory and UI only | Cleared on exit |
| Redacted diagnostics / real-evaluation records | Written only after the user explicitly exports diagnostics or starts a local evaluation capture; never uploaded automatically | User-selected local file or folder |

See [PRIVACY.md](PRIVACY.md) for the complete bilingual privacy notes. Text sent to third-party services is governed by their respective policies.

## Quick start

### Requirements

- Windows 10 22H2 or Windows 11, x64.
- Microsoft Edge WebView2 Runtime, normally present on current Windows installations.
- The smaller build requires .NET 8 Desktop Runtime; the self-contained build does not.
- Borderless windowed mode is recommended. Third-party overlays are not guaranteed in exclusive fullscreen.

### Download 2.0

Open the [v2.0.0 release page](https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases/tag/v2.0.0), download `RiftLens-2.0.0-win-x64.zip`, extract the complete folder, and run `RiftLens.exe`. Keep the packaged `WebUI` folder beside the executable.

### Run from source

```powershell
git clone https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual.git RiftLens
cd RiftLens
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./run.ps1
```

`build.ps1` builds the app and test project, then runs the local regression suite. It never asks for or reads your translation API key.

### First run

1. Select only the chat or on-screen text area you want RiftLens to recognize.
2. Wait for the bundled OCR status to become ready.
3. Configure an OpenAI Chat Completions-compatible endpoint, model, and your own API key.
4. Select **Start translating**, then return to the game. The first scan establishes a baseline and does not read old messages aloud.
5. Compare the “OCR source” and translation columns on the History page to tune HUD scale, chat font size, and selection bounds.

> [!NOTE]
> RiftLens is BYOK (Bring Your Own Key). It does not provide, share, or preconfigure a DeepSeek/OpenAI key. DeepSeek is only the default; the compatible endpoint and model are configurable.

## Interface

<table>
  <tr>
    <td width="50%"><img src="assets/readme/history.png" alt="Translation history with separate source and translation columns"></td>
    <td width="50%"><img src="assets/readme/voice.png" alt="Natural speech settings"></td>
  </tr>
  <tr>
    <td align="center"><strong>Translation history</strong><br>Search, filter, scroll, expand, and compare OCR source with translation</td>
    <td align="center"><strong>Natural speech</strong><br>Control volume, speech density, tactical signals, and emotion handling</td>
  </tr>
</table>

The home page keeps onboarding to three steps: recognition region, API, and start. Detailed filters, overlay behavior, runtime options, and language live in Settings. The main UI is packaged local WebView2 content; in-game overlays remain lightweight native windows.

## Architecture

```text
RiftLens/
├─ Models/       Settings, regions, and translation contracts
├─ Services/     Capture, preprocessing, OCR, deduplication, classification, AI, and speech
├─ UI/           WinForms shell, region selector, translation overlay, and floating control
├─ WebUI/        Locally packaged HTML, CSS, and JavaScript UI
└─ Assets/       Champion dictionary and Korean/Chinese/English OCR models

RiftLens.SelfTest/
├─ Program.cs                    Core and boundary regression tests
└─ webui-interaction-test.js     Real click tests for Chinese and English UI
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for responsibilities, thread boundaries, and extension points. The project intentionally remains two .NET projects with no server, database, or heavy frontend toolchain.

## Build and test

```powershell
# Build and run core self-tests
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1

# Run bilingual WebUI click tests separately (Microsoft Edge required)
node ./RiftLens.SelfTest/webui-interaction-test.js

# Smaller output; .NET 8 Desktop Runtime required on the target PC
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1

# Larger standalone output
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1 -SelfContained
```

Outputs go only to the Git-ignored `artifacts/` directory. Windows CI builds, runs core tests, checks both UI languages, and produces a downloadable build artifact without publishing a release.

## Current limitations

- OCR accuracy varies with chat size, HUD scale, moving backgrounds, font outlines, stream compression, and selected bounds; no fixed accuracy rate is claimed yet.
- Classification favors avoiding system-message speech, but new client versions and custom localization may still introduce unknown formats.
- Regions currently use absolute screen coordinates and may need re-selection after display, resolution, or window-position changes.
- AI translation and neural speech depend on the user's network and third-party service availability.
- This remains a personal early MVP. The 2.0 package passes the included automated checks, but users should still scan downloads with current antivirus software and expect real-match edge cases.

## Game compatibility and disclaimer

RiftLens processes screen pixels only. It does not read or modify the game process, inject code, capture game network traffic, automate controls, or send messages. These boundaries reduce integration and performance risk, but **they are not Riot Games approval and do not guarantee compatibility, account safety, or zero ban risk**. Rules and anti-cheat behavior can change; users are responsible for checking current Riot Games terms, platform rules, and local law.

RiftLens isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

League of Legends, champion names, Data Dragon data, and related properties belong to Riot Games or their respective owners. This repository contains no game-client assets; champion-name mappings are used only for interoperability and contextual translation.

## License and third parties

Original RiftLens source code is licensed under the [MIT License](LICENSE), Copyright © 2026 Nixz. Third-party software and data retain their own licenses, including PaddleOCR models, Microsoft ONNX Runtime, Tesseract, tessdata_fast, the Tesseract .NET wrapper, Leptonica, NAudio, Microsoft WebView2, and Riot Data Dragon. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

When Microsoft online speech is used, its voices and service remain subject to Microsoft's terms and availability. RiftLens does not own or resell those voices.

## Contributing and security

- Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.
- Follow [SECURITY.md](SECURITY.md) for sensitive reports. Never paste keys, private chat, or local paths into a public issue.
- See [CHANGELOG.md](CHANGELOG.md) for notable changes.

Independent developer: **Nixz** (GitHub [@Nixz0824](https://github.com/Nixz0824)). Issues and focused pull requests are welcome; please keep the project small and understandable.
