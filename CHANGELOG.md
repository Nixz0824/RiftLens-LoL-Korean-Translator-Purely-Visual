# Changelog

All notable user-facing changes will be documented here. The project follows a lightweight form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and has not yet promised semantic-version compatibility.

## [Unreleased]

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

RiftLens 2.0.0 is the first numbered public MVP release. It passes the repository's automated Windows checks, but it remains a personal early-stage project: real-match OCR, client wording, antivirus reputation, and unusual Windows/HUD configurations will continue to need community testing.
