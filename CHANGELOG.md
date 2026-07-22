# Changelog

All notable user-facing changes will be documented here. The project follows a lightweight form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and has not yet promised semantic-version compatibility.

## [Unreleased]

### Added

- Chinese and English desktop interface with shared output-language behavior.
- User-selected recognition and translation display regions.
- Bundled Korean and English Tesseract OCR data.
- Rolling-chat deduplication, partial-line filtering, timestamp cleanup, and stable-frame OCR.
- Local classification for player chat, tactical signals, kills, system events, and unknown text.
- Per-speaker turn aggregation with faithful, toxicity-aware AI summaries.
- OpenAI-compatible translation with DeepSeek defaults and BYOK credential storage.
- Chinese Xiaoxiao and English Jenny online neural speech with local Windows fallback.
- Click-through translation overlay and compact pause/volume floating control.
- Session-only translation history with separate OCR and translation columns.
- Automated core regression checks and bilingual WebUI interaction tests.

### Security

- API keys are stored in Windows Credential Manager rather than settings JSON.
- Screenshot processing stays in local memory; only classified text crosses the translation network boundary.

## Release policy

The first numbered release will be created after real-match OCR measurements, clean-machine installation checks, current antivirus scans, and a final third-party-license review. Until then, the default branch should be treated as an MVP development snapshot.
