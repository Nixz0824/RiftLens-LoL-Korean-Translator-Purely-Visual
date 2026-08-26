# Changelog

All notable user-facing changes will be documented here. The project follows a lightweight form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and has not yet promised semantic-version compatibility.

## [Unreleased]

## [2.3.0] - 2026-08-26

### Added

- An opt-in `DeepSeek V4 Vision (experimental)` recognition mode. After explicit screenshot consent, only the user-selected crop is sent to `deepseek-v4-flash-vision-exp` for literal Korean/Chinese/English transcription; the existing local classification, terminology, translation, aggregation, and speech pipeline remains unchanged.
- Automatic per-frame fallback to bundled local PP-OCRv5 when a Vision request fails.
- A deterministic Vision post-processing layer that joins wrapped chat rows, rejects scrolled-off orphan fragments, repairs evidence-backed player separators, and confines fuzzy official-term correction to structured HUD/Ping fields.
- Official Riot Data Dragon `zh_TW` names for all 173 champions plus Traditional Chinese abilities, summoner spells, items, and runes. Regional aliases map to the same stable champion/term IDs as Korean, Simplified Chinese, and English.
- A local-only large-obstruction preflight that runs before JPEG encoding or any DeepSeek Vision request. Clearly covered frames are skipped without uploading.
- Constrained OCR near-miss recovery for official Ping/HUD phrases, a translation-time fuse that blocks shop/KDA/HUD leaks, session-locked champion names inside HUD bodies, and per-signal speech cooldowns.
- Simplified-Chinese HUD shares such as `使用了闪现`, `购买了控制守卫`, ASCII-comma caution pings, plating-turret marks, gold ticks, and first-blood/double-kill feed variants are classified locally instead of being spoken as teammate chat.
- Player flash-timer slang such as `1213mid`, `933 sup`, and `mid f on` is interpreted locally as “that lane used or still has Flash” instead of being read as a raw number plus role.
- Clicking an enemy item bar now speaks as “A said B bought [official item]” using the Data Dragon name, including smashed OCR such as `이텔리아주문포식자`.
- A named mutex so a second launch focuses the existing window instead of creating another tray icon.

### Changed

- Vision mode now classifies the local OCR first. Deterministic HUD/Ping/system lines skip the network; only unresolved player chat is sent to DeepSeek, and in-flight Vision work keeps only the latest frame. Uploads use JPEG at native crop size instead of an upscaled PNG.
- The API Key field is permanently password-masked, has no reveal control, and is cleared from the WebUI immediately after the save message is sent. Persistent storage remains Windows Credential Manager only.
- Settings schema is now `11` for the experimental recognition mode and explicit Vision screenshot consent. Existing users remain on local OCR with consent off.
- Chinese neural speech is voice-locked to Xiaoxiao. A transient failure skips the item and retries after a short cooldown instead of silently switching to another installed Chinese voice; English keeps its previous local fallback.
- Local history JSONL now records the actual recognition engine (`deepseek-v4-flash-vision-exp`, local OCR, or Vision fallback) so future A/B evaluations can distinguish pipelines.
- In Vision mode, bundled PP-OCRv5 runs first. DeepSeek is used only when player chat remains unresolved; local output still supplies geometry, colour, fallback, and constrained corroboration for champion/header/HUD fields. Free-form player text is never replaced from local OCR.
- Traditional Chinese Taiwan/Hong Kong/Macau client signals, HUD shares, objective notices, quest notices, kill-spree text, and item markers are normalized before deterministic classification instead of being sent to AI as player chat.
- The final speech gate now silences pure-number OCR, incomplete/low-information text, and Chinese translations that retain substantial Korean text. Local history now says “queued for speech” for spoken rows instead of the misleading “text only” label.

### Fixed

- Flash OCR near-misses such as `정멸`/`점렬`/`정렬`, control-ward purchases OCR'd as `져야 와드`, and `드레이크` respawn timers are classified as HUD instead of being spoken as invented champion names, surrenders, or item names.
- Unresolved smashed hero-plus-item OCR is silenced instead of being guessed by the translator.
- One-syllable Korean glossary entries such as `용` no longer match inside an unrelated word ending such as `세용`; legitimate phrase starts such as `용앞` remain supported.
- Regional and one-character OCR variants such as `犽宿`, `夢宿`, and `弗宿` resolve to the official Yasuo ID and are spoken as `亚索`, while `凱莎`, `烏迪爾`, `伊瑞莉雅`, and other `zh_TW` names resolve through the same catalog.
- Match-history timestamps/KDA, pure numbers, and known shop/stat labels no longer enter the player-chat path through a clock colon.
- Welcome and first-run guide screens advance automatically; the extra “click anywhere to continue” prompt is gone.

### Privacy

- Local OCR remains the default. Vision never runs until the user selects the experimental mode and consents to sending the selected crop.
- Accepted Vision uploads are JPEG of the selected region at native size. HUD-only frames stay local. The full screen is never sent.
- The API Key field stays password-masked with no reveal control, is cleared from the WebUI after save, and remains in Windows Credential Manager only.
- Release notes and packages contain no local evaluation captures, chat logs, annotations, machine paths, API keys, or personal test data.

## [2.2.0] - 2026-08-24

### Added

- A new light, single-page desktop interface based on the maintainer's Figma layout, with a product welcome screen on every launch and a one-time three-step introduction for first use.
- Eight complete light-interface palettes plus a custom colour picker. Cards, controls, sliders, and the native floating control share the selected accent and surface colours.
- Optional local translation-history saving. It is off by default and writes JSONL only under the current user's RiftLens local-data directory after the user enables it.
- Chinese and English interaction coverage for onboarding, palette changes, settings, history saving, and content-driven card layout.

### Changed

- The WebUI now fills the native window directly instead of nesting a second rounded page inside an outer canvas. Windows DWM owns the antialiased outer corners.
- The home screen combines API connection, voice controls, system information, translation history, and quick actions in one formal workspace. Detailed runtime and speech options remain in the settings popover.
- Chinese and English cards use content-driven heights instead of separate fixed row sizes; shorter Chinese copy no longer leaves large empty card areas, while longer English copy can grow without clipping.
- Settings and advanced settings close when the user clicks elsewhere in the application, while internal clicks and the colour picker remain usable.
- The floating control was simplified to pause/resume, volume, and drag. Its compact and expanded surfaces now follow the selected main-interface palette.
- The window close control honours “close to tray” directly, and the form-closing safeguard covers ordinary close paths without intercepting Windows shutdown or Task Manager termination.
- Settings schema is now `10` for onboarding state, interface palette, custom colour, and optional local-history saving.

### Fixed

- Removed the duplicate native loading page that appeared before the product-owned welcome screen.
- Removed jagged double rounding caused by combining a WinForms clipping region, DWM corners, and an inner WebUI radius.
- Fixed high-DPI clipping in the voice and system-information cards, long build metadata in the displayed version, compressed update buttons, and English text crowding.
- Fixed the floating volume row's vertical alignment, transparent-window percentage rendering, speaker/track collision, and duplicate action icons.
- Closing the WebUI window with close-to-tray enabled now hides the main window while keeping the process and tray entry alive.

### Privacy

- The pure-visual and BYOK boundaries are unchanged: no game-memory access, injection, packet capture, input simulation, automatic chat sending, bundled key, or key written to settings/README.
- Release notes and packages contain no local evaluation captures, chat logs, annotations, machine paths, API keys, or personal test data.

## [2.1.0] - 2026-08-18

Implementation brief: `docs/2.1-PLAN.md`.

### Added

- Window-relative capture boxes. If the League client is visible, the box is stored as fractions of that client area and resolved again on each capture.
- Real default presets for in-game chat, champion select, and a bottom subtitle band.
- Overlay control actions for pause, mute, and reselect while the pill is expanded.
- A one-line “text only” reason on the overlay and in the latest-result status when speech is refused.
- One extra aggregation wait when the last OCR line looks incomplete.
- A user-triggered redacted diagnostic export. It stays in memory until explicitly saved, contains no screenshots or API key, and replaces recognized player IDs with session aliases.
- An opt-in, local-only real-evaluation capture mode for future maintainer testing. It is off by default, shows a privacy warning, records only the selected region, and never uploads automatically.

### Changed

- The launcher WebUI stays on the 2.0 visual. 2.1 is the capture, translation, speech, and overlay pipeline.
- Settings schema is now `9`. Empty name-only presets are replaced with the real default ratios.
- The translation model is asked only for `displayLines` and a toxicity flag. Spoken text, intent, priority, and the speak/silence decision are assembled locally, then rechecked by `TranslationSemanticGuard` and `FinalSpeechPolicy`.
- Diagnostics and the honest local-build version control live on the Settings page.
- Fixed-format Korean, Chinese, and English Ping/HUD messages are classified locally before free-form AI translation, including ability and summoner-spell markers, health/mana shares, item-gold needs, ward states, objective respawn timers, and common tactical signals.
- System-speech switches now describe map objectives and structures, wellness reminders, and match-flow/system notices more precisely. Player-authored chat remains translated by default.
- Cross-frame deduplication uses a stable event key with limited OCR tolerance while preserving genuinely repeated Pings.
- ONNX Runtime keeps CPU memory-arena reuse but disables fixed-shape memory patterns to reduce retained memory across variable chat crops.

### Fixed

- Long-match `minutes:seconds` timestamps, full-width punctuation, missing brackets, and limited OCR damage no longer let the clock colon become a false player/content separator.
- Tactical signals and HUD shares no longer use “teammate says” wording. Known initiators mark/request/signal; unattributed game text uses passive wording.
- A final speech policy rechecks the latest category switches and blocks fixed HUD/system text that accidentally reaches the free-chat path.
- A bare `champion + R/Q/W/E` marker stays a marker instead of becoming an unsupported cooldown or “ability used” claim.
- Residual timestamps and player IDs are removed before speech; fixed countdown separators are no longer read as negative numbers.

### Evaluation

- The same captured real-use session was replayed before and after the routing changes. Timestamp-prefix recall moved from `3.95%` to about `99.2%` (`+95.3` percentage points).
- Against 141 manually reviewed labels from that same session, message-routing agreement moved from `36.88%` to `141/141`. All 24 observed problematic speech cases in the session are now covered by deterministic routing and the final speech gate.
- On the same local screenshot probe, working set moved from about `250.28 MB` to `183.00 MB` (about `27%` lower), private memory from `249.67 MB` to `165.17 MB` (about `34%` lower), and OCR P50 from `1064.16 ms` to `1051.06 ms` (no measured slowdown).
- These are local, single-session replay results—not a general accuracy promise. Raw captures, chat logs, annotations, replay outputs, local paths, and FPS/frametime measurements are not included in the repository. OCR recognition quality itself was not the primary change in this pass.

## [2.0.0] - 2026-07-26

### Added

- Chinese and English desktop interface with shared output-language behavior.
- User-selected recognition and translation display regions.
- Bundled PP-OCRv5 Mobile text detection plus Korean-English and Chinese-English recognition through CPU-only ONNX Runtime.
- Word-segment, confidence, and Unicode-script-aware fusion for Korean, Chinese, and English in the same screenshot or line, with the prior Korean-English Tesseract engine retained as a local fallback.
- Rolling-chat deduplication, partial-line filtering, timestamp cleanup, and stable-frame OCR.
- Structure-first, colour-assisted local classification for player chat, tactical signals, kill events, neutral objectives, client/wellness reminders, other system notices, and unknown text.
- Independent safe-by-default switches for each optional system-message category; player chat remains always translated.
- Urgent, key, and neutral player-speech switches, with urgent warnings able to pre-empt lower-priority audio while timestamps, player IDs, and pure abuse remain silent.
- Per-speaker turn aggregation with one-to-one literal display translations for consecutive messages.
- A lightweight semantic event layer for LoL markers, status shares, cooldowns, requests, plans, warnings, and explicit actions.
- Deterministic Korean, Chinese, and English handling for player-prefixed Ping/HUD messages before they can enter free-form AI chat interpretation.
- Local AI-summary validation for exact evidence fragments, numbers, negation, countdowns, and ongoing-action claims; unsupported narration is silenced while literal overlay text remains available.
- Per-session player-ID-to-champion learning: only official champion-name matches vote, a repeated majority locks the pairing, and later OCR fluctuations are corrected locally.
- Embedded Data Dragon 16.14.1 Korean/Chinese/English alignment for 1,400+ champion abilities, summoner spells, items, and runes, plus common player shorthand.
- OpenAI-compatible translation with DeepSeek defaults and BYOK credential storage.
- Chinese Xiaoxiao and English Jenny online neural speech with local Windows fallback.
- Anti-aliased layered translation and control overlays, a fixed-size custom translation region, speaker-grouped line layout, a 7-second display duration, and a draggable expanded control.
- A new AI-generated cyan/blue/violet/pink lens mark with dedicated 16-256 px Windows icon frames.
- Session-only translation history with separate OCR and translation columns.
- Automated core regression checks and bilingual WebUI interaction tests.

### Fixed

- HUD separators such as `Flash - 20s` are treated as separators rather than negative signs.
- Objective or enemy-status pings are no longer promoted into claims that a teammate is actively taking an objective or performing another action without explicit text evidence.
- Player-authored sentences that merely mention retreat, objectives, or other tactical words remain in the normal player-chat path unless they match a deterministic Ping/HUD form.

### Security

- API keys are stored in Windows Credential Manager rather than settings JSON.
- Screenshot processing stays in local memory; only classified text crosses the translation network boundary.

## Release policy

RiftLens 2.3.0 is the current public MVP release. Local OCR remains the default; DeepSeek V4 Vision is an opt-in experiment. The package passes the repository's automated Windows checks, but it remains a personal early-stage project: real-match OCR, client wording, antivirus reputation, and unusual Windows/HUD configurations will continue to need community testing.
