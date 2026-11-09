# 迁移记录：RiftLens 项目位置变更（2026-10-17）

> 本文是本项目地址变更的权威记录。凡涉及“项目路径”的文档、脚本、后续 AI 交接，一律以本文为准。

## 1. 变更摘要

| 项 | 旧地址 | 新地址 |
|---|---|---|
| 项目根目录 | `C:\Users\98084\Documents\LOL韩服局内聊天翻译工具` | `D:\python项目\RiftLens` |
| 仓库远端 | `https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual.git` | 不变 |
| 分支 / HEAD | `main` @ `314fb55 Release RiftLens 2.3.0` | 不变 |

变更原因：C 盘容量归置，项目统一收拢到 `D:\python项目\` 下作为独立项目管理。项目本体（.NET + WebView2）与 Python 无关，目录归属只是仓库层级约定。

## 2. 迁移方式

- 工具：`robocopy <旧路径> <新路径> /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /MT:16 /XJ`
- 范围：整棵树（含 `.git`、未提交改动、未跟踪文件、`.tools` 本地工具链、`artifacts` 历史产物），不做任何裁剪。
- 保留：文件时间戳、目录时间戳、隐藏属性。
- **未执行**任何 git 操作（无 `clone`、无 `checkout`、无 `clean`）：未提交改动与未跟踪文件原样保留。

## 3. 校验结果

| 校验项 | 结果 |
|---|---|
| 目录数 | 7046（失败 0） |
| 文件数 | 26391 → 26391 |
| 总体积 | 5.701 GB → 5.701 GB |
| 相对路径差异 | 缺失 0 · 多余 0 |
| 逐文件大小差异 | 0 |
| 内容 SHA256（排除 `bin/`、`obj/`、`.tools/`，共 26105 个文件 / 4542.7 MB） | 全部一致，0 处不匹配 |
| `git status` | 与旧目录完全一致（11 个已修改、4 个未跟踪） |
| `git log -1` | `314fb55 Release RiftLens 2.3.0` |

`bin/`、`obj/`、`.tools/` 内的文件同样按“路径 + 字节数”逐项核对通过；它们属于可再生产物，若怀疑状态陈旧可执行 `dotnet clean` 后重编。

## 4. 路径无关性确认

以下脚本均以 `$PSScriptRoot` 推导项目根，迁移后无需改动，也不含硬编码旧路径：

- `build.ps1`（本地 `.tools\dotnet\dotnet.exe` 优先，回退全局 `dotnet`）
- `run.ps1`（同上）
- `publish.ps1`
- `NuGet.Config`（`globalPackagesFolder` 使用相对路径 `.tools/nuget-packages`，随项目一起搬走，离线包缓存仍然有效）
- `Directory.Build.targets`、`global.json`（SDK 8.0.423，`rollForward: latestMajor`）

## 5. 已更新的文档

| 文件 | 改动 |
|---|---|
| `RIFTLENS_AI交接文档.md` | 第 5 行「项目路径」改为新地址并指向本文 |
| `CLAUDE_CODE_项目交接与开发前言.md` | 第 4 行「项目绝对路径」改为新地址并指向本文 |
| `docs/MIGRATION-2026-10-17.md` | 新增（本文） |

说明：`artifacts/performance-2026-09-05/`、`artifacts/review-2026-09-05/` 下的 `LOG`、`favorites_diagnostic.log` 等文件里出现的旧绝对路径是 **2026-09-05 当时运行留下的历史日志**，属于证据留痕，保持原样不改写；`README.md` 与 `RiftLens/WebUI/app.js` 中的「LOL韩服」是产品名称文案，与路径无关。

## 6. 本机运行前提

- .NET SDK：项目自带 `.tools\dotnet`（8.0.423）；本机亦装有全局 SDK。
- WebView2 Runtime：主界面依赖，缺失时需安装 Evergreen Runtime。
- 运行时代数据仍写在 `%LocalAppData%\RiftLens\`（OCR 模型、`settings.json`），**不随项目迁移**；API Key 存于 Windows 凭据管理器，同样与项目路径无关。

## 7. 旧地址处置

旧目录 `C:\Users\98084\Documents\LOL韩服局内聊天翻译工具` 已于 2026-10-17 在校验与编译验证通过、并经用户确认后删除（删除时断言文件数 26391、源头目录确为 RiftLens 工程根）。

原位置保留的说明文件：

```
C:\Users\98084\Documents\LOL韩服局内聊天翻译工具_已迁移到D盘.txt
```

除该说明文件外，C 盘不再保留项目副本。

## 8. 后续注意

- 任何绝对路径引用（IDE 工作区、外部书签、自动化脚本）都需重新指向 `D:\python项目\RiftLens`。
- 推送到远端前请先确认 11 个已修改文件与 4 个未跟踪文件是否要一并提交；本次迁移不改变它们的状态。
