# Changelog

All notable user-facing changes will be documented here. The project follows a lightweight form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and has not yet promised semantic-version compatibility.

## [Unreleased]

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

RiftLens 2.1.0 is the current public MVP release. It passes the repository's automated Windows checks, but it remains a personal early-stage project: real-match OCR, client wording, antivirus reputation, and unusual Windows/HUD configurations will continue to need community testing.
