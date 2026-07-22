# Contributing to RiftLens

Thanks for helping. RiftLens is deliberately a small personal Windows utility, so focused fixes and measurable improvements are preferred over large framework migrations or new backend services.

## Before opening a change

1. Search existing issues and describe the real use case.
2. For OCR or classification changes, include redacted sample text or synthetic fixtures—never publish player names, API keys, private chat, or local account paths.
3. Keep the screen-only boundary: no game-memory reading, injection, packet interception, input automation, or automatic chat sending.
4. Do not add telemetry, advertising, bundled service credentials, or a mandatory cloud account.
5. Preserve both Simplified Chinese and English layouts and behavior.

## Local checks

Requirements: Windows 10/11, .NET 8 SDK, Microsoft Edge, and Node.js for the browser interaction test.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
node ./RiftLens.SelfTest/webui-interaction-test.js
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1
```

When changing OCR or message rules, add a small deterministic case to `RiftLens.SelfTest/Program.cs`. When changing the WebUI, test the same action in both languages and keep all buttons keyboard accessible.

## Pull requests

- Keep one concern per pull request.
- Explain user-visible behavior, privacy/network impact, and verification performed.
- Update `CHANGELOG.md` for notable behavior changes.
- Do not commit `artifacts/`, `.tools/`, local settings, handoff notes, design working files, or editor/assistant state.
- By contributing, you agree that your contribution is licensed under the repository's MIT License. Third-party assets must include a compatible license and attribution.

## Design direction

The desktop UI uses warm off-white surfaces, navy typography, restrained mint/violet accents, and light glass treatment. Preserve alignment, readable contrast, compact game-safe overlays, and the existing three-step onboarding. Avoid adding settings to the home page when they belong in Settings.

## Questions

Use GitHub Discussions if enabled, otherwise open a narrowly scoped Issue. For security-sensitive topics, follow [SECURITY.md](SECURITY.md).
