# Security Policy / 安全说明

## Supported version / 支持范围

RiftLens is currently a personal MVP. Security fixes are applied to the latest commit on the default branch; older source snapshots and third-party forks are not maintained by this repository.

RiftLens 当前是个人开发 MVP。安全修复仅面向默认分支的最新代码；旧源码快照和第三方 Fork 不在本仓库维护范围内。

## Report a vulnerability / 报告漏洞

Please use GitHub's **Private vulnerability reporting** for issues involving credential storage, unintended screenshot or chat disclosure, command/code execution, unsafe update behavior, or dependency compromise. If that option is unavailable, open a public issue containing only a request for a private contact channel—do not disclose the vulnerability details there.

涉及凭据存储、截图或聊天意外泄露、命令/代码执行、不安全更新或依赖投毒的问题，请优先使用 GitHub 的 **Private vulnerability reporting**。如果该功能不可用，请只创建一个要求私下联系的公开 Issue，不要公开漏洞细节。

Never include any of the following in a report:

- a real API key, token, password, email address, or Windows credential export;
- unredacted player chat, screenshots, nicknames, or stream content;
- full local paths containing a Windows account name;
- crash dumps or settings files before checking them for personal data.

请使用无效占位符和最小复现材料。维护者会先确认问题，再讨论修复与披露时间。项目不承诺固定响应时限，但会优先处理可能造成数据或凭据泄露的问题。

## Security boundaries / 安全边界

- API keys are intended to stay in Windows Credential Manager under `RiftLens/TranslationApi`.
- `%LOCALAPPDATA%\RiftLens\settings.json` must never contain an API key.
- Screenshots remain local and in memory unless the user explicitly starts the documented real-evaluation capture and chooses a local folder.
- The app must not read game memory, inject code, simulate input, or automatically send chat.
- Third-party AI and speech providers remain outside this project's security and privacy boundary.

If a fork changes any boundary above, it should update its README and privacy notice before distribution.
