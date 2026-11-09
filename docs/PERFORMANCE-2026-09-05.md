# 性能优化记录 · 2026-09-05

本轮优化 OCR 内存分配与后台界面刷新。模型、识别分辨率、置信度阈值、聊天分类规则及界面视觉保持原有方案。

## 实现

- 韩英、中英识别器共用每个文字片段的缩放图和输入张量，两次模型推理仍顺序执行。
- 在 ONNX 输出的有效生命周期内直接读取连续内存，移除识别及检测输出的整数组复制；非连续张量类型保留复制兜底。
- 复用检测输入、识别输入、检测掩码和连通域队列。缓冲区按当前引擎遇到的最大尺寸保留，处理范围始终使用本帧尺寸。
- 主窗口隐藏时停止序列化和发送 WebView2 状态，恢复显示后发送最新快照。WebView2 自身暂停是尽力执行，状态刷新不依赖暂停成功。
- 历史记录内容和语言未变化时保留已有 DOM 行，无关状态更新不再重建整张表。等长度内容修改、清空与语言切换仍更新。

## OCR 基准方法

同机 .NET 8.0.29、Release 构建；三个固定合成聊天样本，每种图像预热两次，再测量 12 次。模型加载不计入时间。分配量由 `GC.GetTotalAllocatedBytes` 获取，是进程在识别期间的临时托管分配，不是常驻内存或工作集。

初测文件：`artifacts/performance-2026-09-05/before.json`、`after.json`。

复测采用独立输出目录中的原始 OCR 实现，与强制重建后的优化实现比较。下表来自 `baseline-confirmation.json` 和 `optimized-verified.json`；后者包含 `reusesInferenceBuffers: true`，避免把 MSBuild 的旧输出当作优化版。基准工具也验证了相反顺序的图像尺寸及 OCR scale 4 → 3 切换。

| 样本 | 原始每帧分配 | 优化后每帧分配 | 减少 | 原始 P50 | 优化 P50 | 原始 P95 | 优化 P95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 三行韩中英混排 | 26.49 MB | 0.80 MB | 97.0% | 166.30 ms | 154.51 ms | 180.64 ms | 159.30 ms |
| 四行小字号韩文 | 52.20 MB | 1.13 MB | 97.8% | 326.89 ms | 303.36 ms | 365.24 ms | 315.23 ms |
| 八行密集混排 | 75.90 MB | 1.79 MB | 97.6% | 480.58 ms | 446.57 ms | 500.28 ms | 462.68 ms |

MB 使用十进制。每种样本 12 次识别期间的 Gen2 回收次数分别从 72 / 118 / 157 降为 0 / 1 / 1。

三个样本的完整输出校验值在原始和优化实现中一致，包括文字、行位置、置信度与颜色证据。改变尺寸、扩大再缩小输入缓冲区后也一致。

初测与复测的耗时存在明显系统负载波动；复测 P50 约减少 7%，不能据此承诺真实对局固定提速。多轮测量中最稳定的收益是临时分配减少约 97% 以及垃圾回收减少。本轮没有测量游戏 FPS、整局内存峰值、线上翻译延迟或人工标注准确率。

## 界面与回归验证

- 中文、英文各以 150 条历史记录接受 100 次无关状态更新：历史表格 DOM 变更均为 0。
- 等长度历史记录修改、语言切换和清空记录通过。
- 正常 Windows WebView2 实测：隐藏窗口期间 100 次状态更新，恢复后只收到 1 次快照，数值为最新值。
- 带 UI 同步上下文后台任务的关闭验证通过，测试窗口及其浏览器主进程退出。
- Release 全部核心自检通过，双语交互及首次引导流程通过。

## 复现

```powershell
# 在仓库根目录使用固定 PowerShell 7 运行。
& ./.tools/dotnet/dotnet.exe build RiftLens.SelfTest/RiftLens.SelfTest.csproj -c Release --no-restore --nologo -t:Rebuild
& ./.tools/dotnet/dotnet.exe run --project RiftLens.SelfTest -c Release --no-build --no-restore -- --benchmark-ocr artifacts/performance/ocr.json 12
node RiftLens.SelfTest/webui-interaction-test.js
& ./.tools/dotnet/dotnet.exe run --project RiftLens.SelfTest -c Release --no-build --no-restore -- --render-main artifacts/performance/window.png 1240 810
```

原始 OCR 对照源及仅用于测量的 MSBuild 覆盖文件位于本机被 Git 忽略的 `.tools/ocr-perf-baseline/`。使用这种覆盖构建后必须强制重建正常输出，再运行应用或基准。
