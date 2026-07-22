## What changed / 改动内容


## Why / 使用场景


## Verification / 验证

- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`
- [ ] `node ./RiftLens.SelfTest/webui-interaction-test.js` when WebUI changed
- [ ] Chinese and English layout checked when user-facing copy changed

## Boundaries / 边界

- [ ] No key, token, private chat, player identity, personal path, or generated build output is committed.
- [ ] No game-memory access, injection, packet capture, input automation, or automatic chat sending is introduced.
- [ ] Privacy/network behavior and `CHANGELOG.md` are updated when relevant.
- [ ] New third-party code or assets include compatible license information.
