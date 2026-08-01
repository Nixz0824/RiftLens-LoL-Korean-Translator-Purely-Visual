(() => {
  "use strict";

  const stage = document.getElementById("stage");
  const pageMap = { home: "home", history: "history", voice: "voice", output: "voice", settings: "settings", advanced: "settings" };
  let currentPage = "home";
  let state = {
    page: "home", regionReady: false, displayRegionReady: false, ocrReady: false, aiReady: false,
    isRunning: false, isPaused: false, speechAvailable: true,
    statusMessage: "正在读取配置…", regionSummary: "尚未框选区域",
    modelName: "deepseek-chat", recentOriginal: "",
    recentTranslation: "", recentMeta: "",
    volume: 65, speechRate: 1, outputModeIndex: 2,
    autoSpeech: true, changeOnly: true, keepEmotion: true,
    tacticalSignals: false, killEvents: false, objectiveEvents: false,
    clientReminders: false, otherSystemEvents: false,
    speakUrgentPlayerChat: true, speakImportantPlayerChat: true, speakNeutralPlayerChat: false,
    floatingButton: true, lowUsage: true, minimizeAfterStart: true,
    closeToTray: true, overlayOpacity: 68, overlayPlacementIndex: 0, language: "zh-CN",
    evaluationRecording: false, history: []
  };
  let historyFilter = "all";
  let toastTimer = 0;
  const placementLabels = ["自动避让", "识别区上方", "识别区下方", "识别区左侧", "识别区右侧", "自定义区域"];

  // Lightweight desktop localization: one source language, one compact dictionary,
  // and no duplicated HTML tree. Dynamic copy below uses the same dictionary.
  const english = {
    "跳到主要内容": "Skip to main content", "最小化": "Minimize", "最大化": "Maximize", "关闭": "Close",
    "主导航": "Main navigation", "主界面": "Home", "翻译记录": "History", "语音播报": "Speech", "设置": "Settings",
    "更新": "Updates", "软件": "About", "正在翻译": "Translating", "韩文聊天 · 实时识别": "Korean chat · Live recognition",
    "暂停": "Pause", "识别区域": "Recognition region", "已就绪": "Ready", "游戏聊天框": "Game chat box",
    "适用于游戏、选人及直播画面": "Works in game, champion select, and streams", "重选翻译识别区": "Reselect recognition region",
    "框选翻译识别区": "Select recognition region", "框选译文显示区": "Select translation display region",
    "重选译文显示区": "Reselect translation display region", "最近结果": "Latest result", "上路小心": "Careful top",
    "刚刚": "Just now", "已播报": "Spoken", "查看全部记录": "View all history", "连接翻译服务": "Connect translation service",
    "未配置": "Not configured", "已连接": "Connected", "屏显 + 播报": "Overlay + speech", "仅屏显": "Overlay only",
    "仅播报": "Speech only", "保存并连接": "Save & connect", "如何获取 API Key": "How to get an API key",
    "仅保存在 Windows 凭据管理器": "Stored only in Windows Credential Manager", "本次运行中的识别与翻译": "Recognition and translation from this session",
    "条消息": "messages", "条已播报": "spoken", "条已过滤": "filtered", "搜索英雄或内容": "Search champion or message",
    "全部": "All", "高优先级": "High priority", "导出诊断": "Export diagnostics", "清空记录": "Clear history", "今天 · 本次会话": "Today · This session",
    "时间": "Time", "OCR 识别原文": "OCR source", "中文翻译": "Translation", "类型": "Type", "播报": "Speech",
    "普通诊断默认只在内存；真实评估仅在主动开启后保存框选区域截图": "Ordinary diagnostics stay in memory; selected-region screenshots are saved only after you start a real evaluation capture",
    "开始真实评估记录": "Start real evaluation capture", "停止并保存评估记录": "Stop and save capture",
    "把重要信息自然地说出来": "Hear important information in a natural voice", "语音服务可用": "Speech service available",
    "晓晓语音形象": "Xiaoxiao voice profile", "晓晓 Neural": "Xiaoxiao Neural", "Windows 神经语音 · 中文": "Windows neural voice · Chinese",
    "当前音色": "Current voice", "自然、清晰，适合游戏提醒": "Natural and clear for in-game alerts", "试听音色": "Preview voice",
    "快速控制": "Quick controls", "自动播报": "Automatic speech", "播报音量": "Speech volume", "语速": "Speech rate",
    "输出设备": "Output device", "当前使用 Windows 系统默认输出设备": "Uses the Windows default output device", "Windows 系统默认": "Windows default",
    "播报策略": "Speech strategy", "A. 播报方式": "A. Turn handling", "紧急优先 · 重点合并": "Urgent first · Group key messages",
    "危险提醒立即处理；其他有效沟通按发言人短暂合并": "Danger calls skip the wait; other useful messages are briefly grouped by speaker",
    "B. 播报内容": "B. What to speak", "队友聊天": "Team chat", "战术信号": "Tactical signals", "击杀播报": "Kill feed",
    "地图资源与建筑": "Map objectives & structures", "防沉迷提示": "Wellness reminders", "对局流程与系统": "Match flow & system",
    "始终翻译": "Always translated", "玩家聊天语音": "Player chat speech", "紧急提醒": "Urgent alerts",
    "重点沟通": "Key messages", "中性闲聊": "Neutral chat", "系统 Ping 与固定通知 · 默认关闭": "System pings & fixed notices · Off by default",
    "系统 Ping 与固定通知说明": "System pings and fixed notices details",
    "“战术信号”只控制游戏生成的 Ping、战术标记和技能冷却等 HUD 分享，不控制玩家手动输入的战术聊天；其余开关只控制对应的固定系统文本。低置信度内容只显示、不播报。": "Tactical signals controls only game-generated pings, markers, and HUD shares such as cooldowns. It does not control player-typed tactical chat. The other switches control only their matching fixed system text. Low-confidence text is displayed silently.",
    "只控制游戏生成的 Ping、战术标记与 HUD 分享；不控制玩家手动输入的战术聊天": "Controls only game-generated pings, tactical markers, and HUD shares; it does not control player-typed tactical chat",
    "控制固定格式的击杀、第一滴血、多杀、连杀、终结与团灭播报": "Controls fixed-format kills, first blood, multikills, sprees, shutdowns, and ace announcements",
    "控制巨龙、男爵、先锋、巢虫、防御塔、召唤水晶与水晶枢纽等固定提醒": "Controls fixed notices for Dragons, Baron, Herald, Void Grubs, turrets, inhibitors, and the nexus",
    "只控制游戏时长、健康与休息提醒": "Controls only play-time, wellness, and break reminders",
    "控制出兵、掉线重连、投降重开、暂停恢复、聊天限制与信号限频等固定通知": "Controls fixed notices for minion spawn, reconnects, surrender/remake, pause/resume, chat restrictions, and ping rate limits",
    "C. 表达方式": "C. Wording", "保留情绪摘要": "Keep emotional tone", "保留焦急、提醒等语气，不复述辱骂": "Keeps urgency and warnings without repeating abuse",
    "保留情绪，但不朗读脏话": "Keep emotion without reading abuse", "保留情绪，不朗读脏话": "Keep emotion without reading abuse",
    "时间戳、玩家 ID 与纯辱骂不播报": "Never speaks timestamps, player IDs, or pure abuse",
    "中性闲聊也播报": "Also speak neutral chat", "蕾欧娜提醒上路小心，并注意敌方打野。": "Leona warns top to be careful and watch the enemy jungler.",
    "试听这句话": "Preview this line", "队友聊天说明": "Team chat details", "战术信号说明": "Tactical signal details",
    "始终显示翻译；语音按紧急、重点与中性三档分别控制，同一玩家连续内容会短暂合并": "Translations are always shown. Speech is controlled separately for urgent, key, and neutral messages; consecutive lines from one player are briefly grouped.",
    "按你的使用习惯调整 RiftLens": "Tune RiftLens to the way you play", "界面与输出语言": "Interface & output language",
    "界面、翻译与播报语言": "Interface, translation, and speech language", "中文": "中文", "设置会自动保存": "Settings save automatically",
    "翻译服务": "Translation service", "服务商": "Provider", "接口类型": "API type", "当前版本使用 OpenAI 兼容接口": "This version uses an OpenAI-compatible endpoint",
    "模型": "Model", "API 密钥": "API key", "已安全保存": "Saved securely", "尚未配置": "Not configured", "管理密钥": "Manage key",
    "密钥仅保存在 Windows 凭据管理器": "The key is stored only in Windows Credential Manager", "识别与过滤": "Recognition & filtering",
    "消息识别": "Message recognition", "队友个人聊天": "Player chat", "始终识别并翻译": "Always recognized and translated",
    "分类依据": "Classification basis", "聊天结构为主 · 颜色辅助": "Chat structure first · Colour-assisted",
    "仅在画面变化时识别": "Recognize only when the image changes", "播报战术信号": "Speak tactical signals", "保留玩家情绪": "Keep player emotion",
    "颜色不会单独决定类型；可选系统播报请在“语音播报”页逐类开启": "Colour never decides a type on its own. Enable optional system categories individually on the Speech page.",
    "屏显与悬浮": "Overlay & floating control", "输出方式": "Output mode", "显示位置": "Display position", "自动避让　⌄": "Auto avoid ⌄",
    "文字透明度": "Text opacity", "翻译文字透明度": "Translation text opacity", "显示悬浮控制按钮": "Show floating control",
    "运行方式": "Runtime", "游戏中保持低占用": "Keep resource use low in game", "启动后最小化": "Minimize after starting",
    "开机自动启动": "Start with Windows", "关闭窗口时缩小到托盘": "Close window to tray", "OCR 模型": "OCR model",
    "韩文 + 英文 · 已内置": "Korean + English · Bundled", "韩中英 PP-OCRv5 · 已内置": "Korean, Chinese & English PP-OCRv5 · Bundled", "始终启用": "Always on", "暂未提供": "Not available yet",
    "翻译显示位置": "Translation display position", "恢复默认设置": "Restore defaults", "完成": "Done",
    "晓晓": "Xiaoxiao", "晓晓 · 自然播报": "Xiaoxiao · Natural voice", "全局播报音量": "Global speech volume",
    "本地 OCR": "Local OCR", "AI 在线": "AI online", "正在读取配置…": "Loading settings…", "尚未框选区域": "No region selected",
    "暂无识别内容": "No recognized text", "完成框选后，最新译文会出现在这里。": "The latest translation will appear here after you select a region.",
    "暂无记录": "No history", "待框选": "Select region", "翻译已暂停": "Translation paused", "点击继续 · 恢复识别": "Select Resume · Restart recognition",
    "继续": "Resume", "准备就绪": "Ready", "准备开始": "Get ready", "区域与服务均已就绪": "Region and services are ready",
    "完成下方配置即可使用": "Finish the setup below to begin", "开始翻译": "Start translating", "检查配置": "Check setup",
    "正在安全保存…": "Saving securely…", "请先输入 API Key。": "Enter an API key first.", "标准": "Normal", "较慢": "Slower", "较快": "Faster", "自然": "Natural",
    "克制摘要": "Neutral summary", "只播报战术含义，尽量省略情绪": "Speaks tactical meaning while minimizing emotion",
    "本地 OCR 已就绪": "Local OCR ready", "OCR 初始化中": "OCR initializing", "AI 未连接": "AI disconnected", "语音服务离线": "Speech service offline",
    "共 {count} 条 · 可滚动查看": "{count} total · Scroll to view", "没有符合筛选条件的记录": "No records match these filters",
    "开始翻译后，记录会实时显示在这里": "History will appear here after translation starts", "未播报": "Not spoken",
    "翻译记录，按回车展开完整内容": "Translation record. Press Enter to expand the full text", "紧急": "Urgent", "提醒": "Alert",
    "玩家聊天": "Player chat", "客户端提醒": "Client reminder", "系统通知": "System notice",
    "自动避让": "Auto avoid", "识别区上方": "Above recognition region", "识别区下方": "Below recognition region",
    "识别区左侧": "Left of recognition region", "识别区右侧": "Right of recognition region", "自定义区域": "Custom region",
    "显示或隐藏密钥": "Show or hide key", "更改语言": "Change language"
  };

  const tr = value => state.language === "en-US" ? (english[value] || value) : value;
  const localizedTextNodes = [];
  const localizedAttributes = [];

  function captureLocalizableDom() {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walker.nextNode())) {
      if (node.parentElement?.closest("script, style, svg")) continue;
      const key = node.nodeValue.trim();
      if (english[key]) localizedTextNodes.push({ node, original: node.nodeValue, key });
    }
    document.querySelectorAll("[aria-label], [placeholder], [title], [data-tip]").forEach(element => {
      ["aria-label", "placeholder", "title", "data-tip"].forEach(name => {
        const value = element.getAttribute(name);
        if (value && english[value]) localizedAttributes.push({ element, name, value });
      });
    });
  }

  function applyUiLanguage(language) {
    state.language = language === "en-US" ? "en-US" : "zh-CN";
    document.documentElement.lang = state.language;
    stage.dataset.language = state.language;
    localizedTextNodes.forEach(item => {
      item.node.nodeValue = item.original.replace(item.key, tr(item.key));
    });
    localizedAttributes.forEach(item => item.element.setAttribute(item.name, tr(item.value)));
    document.querySelectorAll("button[data-language]").forEach(button => {
      const active = button.dataset.language === state.language;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    document.querySelectorAll(".voice-avatar, .status-avatar").forEach(element => element.classList.toggle("english-voice", state.language === "en-US"));
    const profileTitle = document.querySelector(".voice-profile-copy h2");
    const profileDetail = document.querySelector(".voice-profile-copy p");
    const statusVoice = document.querySelector(".voice-status strong");
    if (state.language === "en-US") {
      if (profileTitle) profileTitle.textContent = "Jenny Neural";
      if (profileDetail) profileDetail.textContent = "Windows neural voice · English";
      if (statusVoice) statusVoice.textContent = "Jenny · Natural voice";
    } else {
      if (profileTitle) profileTitle.textContent = "晓晓 Neural";
      if (profileDetail) profileDetail.textContent = "Windows 神经语音 · 中文";
      if (statusVoice) statusVoice.textContent = "晓晓 · 自然播报";
    }
  }

  const previewParameters = new URLSearchParams(location.search);
  const previewPage = previewParameters.get("page");
  if (previewPage && pageMap[previewPage]) state.page = pageMap[previewPage];
  if (previewParameters.get("language") === "en-US") state.language = "en-US";

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  Object.entries({
    "change-only": "仅在画面变化时识别",
    "tactical-signals": "战术信号", "kill-events": "击杀播报",
    "objective-events": "地图资源与建筑", "client-reminders": "防沉迷提示",
    "other-system-events": "对局流程与系统", "urgent-player-speech": "播报紧急提醒",
    "important-player-speech": "播报重点沟通", "neutral-player-speech": "播报中性闲聊",
    "settings-emotion": "保留玩家情绪", "low-usage": "游戏中保持低占用",
    "minimize-after-start": "启动后最小化", "close-to-tray": "关闭窗口时缩小到托盘"
  }).forEach(([id, label]) => document.getElementById(id)?.setAttribute("aria-label", label));
  captureLocalizableDom();
  applyUiLanguage(state.language);
  const post = (action, payload = null) => {
    if (window.chrome?.webview) window.chrome.webview.postMessage({ action, payload });
  };

  function fitStage() {
    const scale = Math.min(innerWidth / 1536, innerHeight / 1004);
    const x = Math.max(0, (innerWidth - 1536 * scale) / 2);
    const y = Math.max(0, (innerHeight - 1004 * scale) / 2);
    stage.style.transform = `translate(${x}px,${y}px) scale(${scale})`;
  }
  addEventListener("resize", fitStage, { passive: true });
  fitStage();

  function navigate(name, notify = true) {
    const page = pageMap[name] || "home";
    currentPage = page;
    stage.dataset.page = page;
    $$(".page").forEach(el => el.classList.toggle("active", el.id === `page-${page}`));
    $$(".nav-button").forEach(el => el.classList.toggle("active", el.dataset.nav === page));
    const heading = $(`#page-${page} h1`);
    if (heading) heading.setAttribute("tabindex", "-1");
    if (notify) post("navigate", page);
  }

  $$("[data-nav]").forEach(button => button.addEventListener("click", () => navigate(button.dataset.nav)));
  $$("[data-action]").forEach(button => button.addEventListener("click", () => {
    button.classList.remove("action-feedback");
    void button.offsetWidth;
    button.classList.add("action-feedback");
    setTimeout(() => button.classList.remove("action-feedback"), 360);
    post(button.dataset.action);
  }));
  $$("button[data-language]").forEach(button => button.addEventListener("click", () => {
    const language = button.dataset.language === "en-US" ? "en-US" : "zh-CN";
    applyUiLanguage(language);
    applyState({ language });
    post("languageChanged", language);
  }));

  $(".topbar").addEventListener("mousedown", event => {
    if (event.button === 0 && !event.target.closest("button")) post("beginWindowDrag");
  });
  $(".topbar").addEventListener("dblclick", event => {
    if (!event.target.closest("button")) post("maximize");
  });

  function bindRange(id, action, outputId, formatter = value => `${value}%`) {
    const input = document.getElementById(id);
    const output = document.getElementById(outputId);
    if (!input) return;
    const paint = () => {
      const ratio = (Number(input.value) - Number(input.min)) / (Number(input.max) - Number(input.min));
      input.style.setProperty("--value", `${Math.round(ratio * 100)}%`);
      if (output) output.textContent = formatter(Number(input.value));
    };
    input.addEventListener("input", paint);
    input.addEventListener("change", () => post(action, Number(input.value)));
    paint();
  }
  bindRange("global-volume", "volumeChanged", "global-volume-value");
  bindRange("voice-volume", "volumeChanged", "voice-volume-value");
  bindRange("overlay-opacity", "overlayOpacityChanged", "opacity-value");
  bindRange("speech-rate", "speechRateChanged", "speech-rate-value", value => tr(value === 0 ? "标准" : value < 0 ? "较慢" : value > 2 ? "较快" : "自然"));

  function bindToggle(id, action) {
    const input = document.getElementById(id);
    if (input) input.addEventListener("change", () => post(action, input.checked));
  }
  bindToggle("auto-speech", "toggleAutoSpeech");
  bindToggle("urgent-player-speech", "urgentPlayerSpeechChanged");
  bindToggle("important-player-speech", "importantPlayerSpeechChanged");
  bindToggle("neutral-player-speech", "neutralPlayerSpeechChanged");
  bindToggle("tactical-signals", "tacticalSignalsChanged");
  bindToggle("kill-events", "killEventsChanged");
  bindToggle("objective-events", "objectiveEventsChanged");
  bindToggle("client-reminders", "clientRemindersChanged");
  bindToggle("other-system-events", "otherSystemEventsChanged");
  bindToggle("keep-emotion", "keepEmotionChanged");
  bindToggle("change-only", "changeOnlyChanged");
  bindToggle("settings-emotion", "keepEmotionChanged");
  bindToggle("floating-button", "floatingButtonChanged");
  bindToggle("low-usage", "lowUsageChanged");
  bindToggle("minimize-after-start", "minimizeAfterStartChanged");
  bindToggle("close-to-tray", "closeToTrayChanged");
  $$("#output-mode button").forEach(button => button.addEventListener("click", () => {
    state.outputModeIndex = Number(button.dataset.value);
    setSegments("#output-mode button", state.outputModeIndex);
    post("outputModeChanged", state.outputModeIndex);
  }));
  $("#reveal-key").addEventListener("click", () => {
    const field = $("#api-key");
    field.type = field.type === "password" ? "text" : "password";
  });
  $("#save-api").addEventListener("click", () => {
    const key = $("#api-key").value.trim();
    if (!key) { showToast(tr("请先输入 API Key。"), true); $("#api-key").focus(); return; }
    const button = $("#save-api");
    button.disabled = true;
    button.textContent = tr("正在安全保存…");
    post("saveApiKey", { apiKey: key });
    $("#api-key").value = "";
    setTimeout(() => { button.disabled = false; button.textContent = tr("保存并连接"); }, 900);
  });
  $("#overlay-placement").addEventListener("change", event => {
    const value = Number(event.target.value);
    state.overlayPlacementIndex = value;
    post("overlayPlacementChanged", value);
  });

  $$("#history-filter button").forEach(button => button.addEventListener("click", () => {
    historyFilter = button.dataset.filter;
    $$("#history-filter button").forEach(el => el.classList.toggle("active", el === button));
    renderHistory();
  }));
  $("#history-search").addEventListener("input", renderHistory);
  $("#history-list").addEventListener("click", event => toggleHistoryRow(event.target.closest(".history-item")));
  $("#history-list").addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    const row = event.target.closest(".history-item");
    if (!row) return;
    event.preventDefault();
    toggleHistoryRow(row);
  });

  const tooltip = $("#tooltip");
  let pinnedHelp = null;
  const hideHelp = help => {
    if (help && pinnedHelp === help) pinnedHelp = null;
    tooltip.classList.remove("show");
    $$(".help[aria-expanded='true']").forEach(item => item.setAttribute("aria-expanded", "false"));
  };
  const showHelp = help => {
    const r = help.getBoundingClientRect();
    const sr = stage.getBoundingClientRect();
    const scale = sr.width / 1536;
    tooltip.textContent = help.dataset.tip;
    tooltip.style.left = `${Math.min(1536 - 300, (r.right - sr.left) / scale + 10)}px`;
    tooltip.style.top = `${Math.max(10, (r.top - sr.top) / scale - 7)}px`;
    tooltip.classList.add("show");
  };
  $$(".help").forEach(help => {
    help.setAttribute("aria-expanded", "false");
    help.setAttribute("aria-describedby", "tooltip");
    help.addEventListener("mouseenter", () => showHelp(help));
    help.addEventListener("mouseleave", () => {
      if (pinnedHelp !== help) tooltip.classList.remove("show");
    });
    help.addEventListener("focus", () => showHelp(help));
    help.addEventListener("blur", () => {
      if (pinnedHelp !== help) tooltip.classList.remove("show");
    });
    help.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      if (pinnedHelp === help) {
        hideHelp(help);
        return;
      }
      hideHelp();
      pinnedHelp = help;
      help.setAttribute("aria-expanded", "true");
      showHelp(help);
    });
  });
  document.addEventListener("click", event => {
    if (pinnedHelp && !event.target.closest(".help")) hideHelp(pinnedHelp);
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && pinnedHelp) {
      const help = pinnedHelp;
      hideHelp(help);
      help.focus();
    }
  });

  function showToast(message, isError = false) {
    if (!message) return;
    const toast = $("#toast");
    toast.textContent = message;
    toast.style.background = isError ? "rgba(124,37,45,.94)" : "rgba(9,33,49,.92)";
    toast.classList.add("show");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove("show"), 3600);
  }

  function setRange(id, value) {
    const el = document.getElementById(id);
    if (!el || document.activeElement === el) return;
    el.value = String(value);
    el.dispatchEvent(new Event("input"));
  }
  function setChecked(id, checked) {
    const el = document.getElementById(id);
    if (el) el.checked = Boolean(checked);
  }
  function setSegments(selector, value) {
    $$(selector).forEach(button => button.classList.toggle("active", Number(button.dataset.value) === Number(value)));
  }

  function applyState(next) {
    const previousStatus = state.statusMessage;
    state = { ...state, ...next };
    applyUiLanguage(state.language);
    navigate(state.page || currentPage, false);

    const runButton = $(".primary-run");
    const heroTitle = $(".hero-copy h1");
    const heroSub = $(".hero-copy p");
    if (state.isRunning) {
      heroTitle.textContent = tr(state.isPaused ? "翻译已暂停" : "正在翻译");
      heroSub.textContent = tr(state.isPaused ? "点击继续 · 恢复识别" : "韩文聊天 · 实时识别");
      runButton.textContent = tr(state.isPaused ? "继续" : "暂停");
    } else {
      const ready = state.regionReady && state.ocrReady && state.aiReady;
      heroTitle.textContent = tr(ready ? "准备就绪" : "准备开始");
      heroSub.textContent = tr(ready ? "区域与服务均已就绪" : "完成下方配置即可使用");
      runButton.textContent = tr(ready ? "开始翻译" : "检查配置");
    }
    $(".ready-label span").textContent = tr(state.regionReady ? "已就绪" : "待框选");
    $(".ready-label").classList.toggle("not-ready", !state.regionReady);
    $(".recognition-actions .outline-button").textContent = tr(state.regionReady ? "重选翻译识别区" : "框选翻译识别区");
    $(".recognition-actions .text-button").textContent = tr(state.displayRegionReady ? "重选译文显示区" : "框选译文显示区");
    $("#api-state").textContent = tr(state.aiReady ? "已连接" : "未配置");
    $("#api-state").classList.toggle("connected", state.aiReady);
    $("#recent-original").textContent = state.recentOriginal || tr("暂无识别内容");
    renderRecent(state.recentTranslation || tr("完成框选后，最新译文会出现在这里。"));
    $("#recent-time").textContent = (state.recentMeta || tr("暂无记录")).split("·")[0].trim();
    $(".spoken").style.display = /播报|spoken|speech|queued/i.test(state.recentMeta || "") ? "inline-flex" : "none";

    setRange("global-volume", state.volume);
    setRange("voice-volume", state.volume);
    setRange("speech-rate", state.speechRate ?? 1);
    setRange("overlay-opacity", state.overlayOpacity);
    $("#overlay-placement").value = String(state.overlayPlacementIndex || 0);
    setChecked("auto-speech", state.autoSpeech);
    setChecked("tactical-signals", state.tacticalSignals);
    setChecked("kill-events", state.killEvents);
    setChecked("objective-events", state.objectiveEvents);
    setChecked("client-reminders", state.clientReminders);
    setChecked("other-system-events", state.otherSystemEvents);
    setChecked("urgent-player-speech", state.speakUrgentPlayerChat);
    setChecked("important-player-speech", state.speakImportantPlayerChat);
    setChecked("neutral-player-speech", state.speakNeutralPlayerChat);
    setChecked("keep-emotion", state.keepEmotion);
    setChecked("settings-emotion", state.keepEmotion);
    $("#expression-policy-title").textContent = tr(state.keepEmotion ? "保留情绪摘要" : "克制摘要");
    $("#expression-policy-detail").textContent = state.keepEmotion
      ? tr("保留焦急、提醒等语气，不复述辱骂")
      : tr("只播报战术含义，尽量省略情绪");
    setChecked("change-only", state.changeOnly);
    setChecked("floating-button", state.floatingButton);
    setChecked("low-usage", state.lowUsage);
    setChecked("minimize-after-start", state.minimizeAfterStart);
    setChecked("close-to-tray", state.closeToTray);
    setSegments("#output-mode button", state.outputModeIndex);
    $("#settings-model").textContent = state.modelName || "deepseek-chat";
    $(".settings-card .saved").textContent = tr(state.aiReady ? "已安全保存" : "尚未配置");
    $("#ocr-pill").classList.toggle("offline", !state.ocrReady);
    $("#ai-pill").classList.toggle("offline", !state.aiReady);
    $("#ocr-pill").childNodes[1].textContent = `${tr(state.ocrReady ? "本地 OCR" : "OCR 初始化中")} `;
    $("#ai-pill").childNodes[1].textContent = `${tr(state.aiReady ? "AI 在线" : "AI 未连接")} `;
    $(".service-pill").classList.toggle("offline", !state.speechAvailable);
    $(".service-pill").lastChild.textContent = tr(state.speechAvailable ? "语音服务可用" : "语音服务离线");
    const evaluationButton = $("#evaluation-recording");
    evaluationButton.classList.toggle("recording", Boolean(state.evaluationRecording));
    evaluationButton.querySelector("span").textContent = tr(
      state.evaluationRecording ? "停止并保存评估记录" : "开始真实评估记录");
    evaluationButton.setAttribute("aria-pressed", String(Boolean(state.evaluationRecording)));
    renderHistory();

    if (previousStatus && next.statusMessage && previousStatus !== next.statusMessage) showToast(next.statusMessage, /失败|错误|不可用|failed|error|unavailable/i.test(next.statusMessage));
  }

  function renderRecent(text) {
    const el = $("#recent-translation");
    const chineseSplit = text.indexOf("：");
    const englishSplit = text.indexOf(":");
    const split = chineseSplit >= 0 ? chineseSplit : englishSplit;
    if (split > 0 && split < 24) {
      el.textContent = "";
      const em = document.createElement("em");
      em.textContent = text.slice(0, split + 1);
      el.append(em, document.createTextNode(text.slice(split + 1)));
    } else el.textContent = text;
  }

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>'"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" }[c]));
  }
  function timeText(value) {
    const d = new Date(value);
    return Number.isNaN(d.valueOf()) ? "--:--:--" : d.toLocaleTimeString(state.language, { hour12: false });
  }
  function toggleHistoryRow(row) {
    if (!row) return;
    const expanded = row.classList.toggle("expanded");
    row.setAttribute("aria-expanded", String(expanded));
  }
  function renderHistory() {
    const all = Array.isArray(state.history) ? state.history : [];
    const query = $("#history-search").value.trim().toLowerCase();
    const items = all.filter(item => {
      if (historyFilter === "spoken" && !item.spoken) return false;
      if (historyFilter === "priority" && Number(item.priority) < 3) return false;
      return !query || `${item.original} ${item.translation}`.toLowerCase().includes(query);
    });
    $("#stat-total").textContent = all.length;
    $("#stat-spoken").textContent = all.filter(x => x.spoken).length;
    $("#stat-filtered").textContent = state.filteredCount || 0;
    $("#list-count").textContent = tr("共 {count} 条 · 可滚动查看").replace("{count}", all.length);
    const list = $("#history-list");
    if (!items.length) {
      list.innerHTML = `<div class="empty-history">${tr(all.length ? "没有符合筛选条件的记录" : "开始翻译后，记录会实时显示在这里")}</div>`;
      return;
    }
    list.innerHTML = items.map(item => {
      const priority = Math.max(1, Math.min(3, Number(item.priority) || 1));
      const kind = escapeHtml(item.kind || (priority >= 3 ? tr("紧急") : priority === 2 ? tr("提醒") : ""));
      const kindClass = priority >= 3 ? "urgent" : "";
      return `<article class="history-item" tabindex="0" aria-expanded="false" aria-label="${tr("翻译记录，按回车展开完整内容")}">
        <i class="history-dot p${priority}"></i><time class="history-time">${timeText(item.time)}</time>
        <div class="history-cell history-original" title="${escapeHtml(item.original)}"><p>${escapeHtml(item.original)}</p></div>
        <div class="history-cell history-translation" title="${escapeHtml(item.translation)}"><p>${escapeHtml(item.translation)}</p></div>
        <span class="history-kind">${kind ? `<b class="kind-badge ${kindClass}">${kind}</b>` : ""}</span>
        <span class="spoken-state ${item.spoken ? "yes" : ""}"><svg><use href="#i-volume"/></svg>${tr(item.spoken ? "已播报" : "未播报")}</span>
      </article>`;
    }).join("");
  }

  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", event => {
      let incoming = event.data;
      if (typeof incoming === "string") { try { incoming = JSON.parse(incoming); } catch { return; } }
      if (incoming?.type === "state") applyState(incoming.data || {});
      if (incoming?.type === "toast") showToast(incoming.message, incoming.isError);
    });
    post("ready");
  } else {
    applyState(state);
  }
})();
