# RiftLens message-classification PRD

Status: implemented for the current Summoner's Rift rule set (Riot 26.15 / client resources 16.15).

## Product boundary

RiftLens classifies only text found inside the screen rectangle selected by the user. It does not read game memory, inject code, capture packets, simulate input, or send chat messages. OCR screenshots remain local. Player-authored chat may be sent to the user's configured translation API; fixed system messages, Pings, and unknown text are never sent to AI for classification.

## User-visible categories

Player chat is always recognized and translated. The following five optional speech switches are independent and default to off:

| Switch | Controls | Does not control |
| --- | --- | --- |
| Tactical signals | Game-generated Pings, tactical markers, cooldown/status/item/resource HUD shares | Player-typed tactical chat such as “wait for me at Dragon” |
| Kill feed | Champion kills, executions, first blood, double through pentakill, six live spree levels, shutdown, ace, respawn | A player asking “was that a triple kill?” |
| Map objectives & structures | Dragon/Elder, Void Grubs, Herald, Baron, spawn/slain/steal/capture/return, Elemental Rift changes, first turret, turret/plating, inhibitor, nexus, Objective Bounties | A player asking whether Baron has spawned |
| Wellness reminders | Korean/Chinese/English play-time, health, and break notices | Connection or AFK notices |
| Match flow & system | Minion spawn, disconnect/reconnect/loading timeout, surrender/remake/AFK compensation, pause/resume, chat restrictions, Ping rate limits, network notices, selected champion-specific fixed events | Any of the other four categories or player chat |

## Classification contract

1. A dedicated line-start parser removes a valid optional timestamp before any speaker delimiter is considered. It supports full-width colons and evidence-backed OCR damage such as a missing bracket, `I`/`1` bracket confusion, or a missing timestamp colon. A clock-like colon can never enter the colour-backed player fallback.
2. Player structure wins next: optional channel + player ID + optional official champion token + colon/content separator. A middle dot may recover a colon only when the channel or `(champion)` structure already proves a player line.
3. Known game-generated Ping/HUD payloads inside a player-shaped line are extracted before free chat. The result records source type, speech act, subject, status, countdown, modality, confidence, and exact evidence.
4. Other fixed messages use an anchored three-language template catalog and slot extraction. Broad keyword-only classification is not permitted.
5. Colour is supporting evidence only. It can recover a player prefix damaged by OCR, but cannot decide a class by itself.
6. Current Summoner's Rift rules are isolated from deprecated or mode-specific rules. Removed Bait/Hold/Vision Cleared Pings, Atakhan, Hexakill, and similar retained localization strings cannot enter the live rule set.
7. A line without enough structural/template evidence is `Unknown`. Unknown text is shown literally, is never sent to the AI classifier, and is silent by default.

## Source attribution and wording

- `player_authored_chat`: the user typed the content; translation may identify the champion speaker, while timestamps and player IDs remain silent.
- `player_initiated_game_signal`: the game rendered a Ping/HUD share with a structurally known initiator; wording uses actions such as marked, requested, or signalled, never “said”.
- `game_generated_signal`: the screen shows a signal without a reliable initiator; wording is passive, such as “收到撤退信号”.
- `game_system`: fixed kill, objective, wellness, or match-flow text; wording uses the matching system category.
- `unknown`: literal display only and no speech.

## Speech safety gate

The final audio worker re-checks the newest settings immediately before synthesis/playback. A queued system item is discarded when its switch has since been disabled, its routed source no longer matches the category, or classification confidence is below `0.85`. Player-chat speech independently re-checks the urgent/key/neutral switches. Timestamps and player IDs are omitted from spoken text.

## AI boundary

Classification is completed before AI translation. Fixed system messages and system Pings use deterministic local wording. Only structurally confirmed player-authored free chat reaches the user's OpenAI-compatible endpoint. AI may translate and summarize player chat but cannot change the source category or bypass the final speech gate.

## Regression evidence

`RiftLens.SelfTest/Fixtures/system-message-regression.json` is retained as the 45-case v1 seed. `RiftLens.SelfTest/Fixtures/evaluation/evaluation-dataset-v2.json` is the current 140-case fixed benchmark: 35 player-chat cases, 35 tactical-signal cases, and 10–20 cases for each remaining group. It covers Korean, Simplified Chinese, English, mixed language, 1080P/2K, 100%/125% UI scale, low contrast, partial rows, and repeated scrolling. Codex completed the first-pass internal annotation under the maintainer's explicit authorization; the manifest records v1/v2 additions and removals.

These are synthetic regression fixtures, not real user incident screenshots. Real screenshots should be added only when a user deliberately supplies and approves a redacted crop; RiftLens must not silently persist captured game images.

Run `dotnet run --project RiftLens.SelfTest/RiftLens.SelfTest.csproj -c Release -- --evaluate-dataset` for the non-CI synthetic rendering/OCR benchmark. It writes an ignored local report under `artifacts/evaluation/`; it does not save screenshots.
