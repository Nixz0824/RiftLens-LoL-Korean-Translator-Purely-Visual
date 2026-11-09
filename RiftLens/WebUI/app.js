(() => {
  "use strict";

  const $ = selector => document.querySelector(selector);
  const $$ = selector => [...document.querySelectorAll(selector)];
  const preview = new URLSearchParams(location.search);
  const english = {
    "跳到主要内容":"Skip to main content","产品特点":"Product features","“看直播、玩韩服，看不懂玩家发言？”":"“Watching streams or playing on KR, but can’t understand player chat?”",
    "纯视觉":"Screen only","零风险":"Zero risk","轻量化":"Lightweight","不干扰":"Low disruption","可文字":"On-screen text","可播报":"Voice output",
    "三步开启":"Get started in three steps","连接翻译服务":"Connect translation service","输入自己的 API Key，连接用户选择的翻译服务。":"Enter your own API key to connect the translation service you choose.","API Key 只保存在 Windows 凭据管理器":"The API key stays in Windows Credential Manager only",
    "框选识别区域":"Select recognition region","只圈住聊天内容，减少动态背景对 OCR 的影响。":"Select only chat content to reduce OCR interference from animated backgrounds.","开始使用后，在游戏画面上完成框选":"Select the region on the game screen after setup",
    "框选翻译显示区域":"Select translation display region","决定译文出现的位置，字幕不会抢走游戏焦点。":"Choose where translations appear without taking focus from the game.","可单独指定译文固定显示的位置":"You can set a dedicated fixed display area",
    "RiftLens 首页":"RiftLens home","窗口控制":"Window controls","最小化窗口":"Minimize window","打开系统设置":"Open system settings","关闭窗口":"Close window","首页快速操作":"Home quick actions","连接、语音与系统信息":"Connection, voice, and system information",
    "LOL韩服·消息翻译工具":"LoL KR · Chat Translation Tool","连接api":"Connect API","框选识别区":"Select recognition region","框选译文区":"Select display region","开始翻译":"Start translating","暂停":"Pause","继续":"Resume","停止":"Stop",
    "API连接":"API connection","如何获取API Key?":"How do I get an API key?","保存并连接":"Save & connect","已连接":"Connected","未配置":"Not configured",
    "语音播报":"Voice narration","Windows神经语音-晓晓":"Windows neural voice · Xiaoxiao","自动播报":"Auto narration","播报音量":"Volume","播报语速":"Speech rate","信号播报":"Tactical signals","输出设备":"Output device","系统默认":"System default","标准":"Normal","较慢":"Slower","较快":"Faster","自然":"Natural","“蕾欧娜提醒上路小心，并注意敌方打野”":"“Leona warns top to be careful and watch the enemy jungler.”","试听":"Preview",
    "系统信息":"System information","作者：github/Nixz0824":"Author: github/Nixz0824","此软件作为个人开源实践项目，未经明确授权拒绝转载、商用。":"This software is a personal open-source practice project. Redistribution or commercial use without explicit authorization is prohibited.","识别：本地 PP-OCRv5（韩/中/英）":"Recognition: local PP-OCRv5 (KR/CN/EN)","识别：DeepSeek V4 Vision（实验，韩/中/英）":"Recognition: DeepSeek V4 Vision (experimental, KR/CN/EN)","检查更新":"Check for updates",
    "翻译记录":"Translation history","翻译记录操作":"Translation history actions","本次翻译记录，可滚动":"Translation history for this session, scrollable","时间":"Time","OCR识别原文":"OCR source","翻译内容":"Translation","是否播报":"Spoken","已播报":"Spoken","未播报":"Silent","暂无记录":"No records yet","开启本地保存":"Enable local saving","停止本地保存":"Stop local saving","清空记录":"Clear history","导出诊断":"Export diagnostics","开始真实评估记录":"Start real evaluation capture","停止并保存评估记录":"Stop and save evaluation capture",
    "系统设置":"System settings","启动后最小化":"Minimize after start","关闭窗口时缩小到托盘":"Close window to tray","显示悬浮控制按钮":"Show floating controls","界面配色":"Interface palette","选择整套界面配色":"Choose an interface palette","瓷白":"Porcelain","黑白":"Black & white","纯白":"Pure white","珊瑚":"Coral","雾蓝":"Mist blue","青绿":"Teal","鼠尾草":"Sage","暖砂":"Warm sand","自定义":"Custom","自定义主题颜色":"Custom theme color","更多设置":"More settings","返回系统设置":"Back to system settings",
    "识别引擎":"Recognition engine","识别模式":"Recognition mode","本地 OCR（默认）":"Local OCR (default)","DeepSeek V4 Vision（实验）":"DeepSeek V4 Vision (experimental)","Vision 模式会把用户框选区域截图发送给 DeepSeek；不会发送全屏，也不会读取游戏内存。":"Vision sends the user-selected crop to DeepSeek. It never sends the full screen or reads game memory.","我同意将框选区域截图发送给 DeepSeek Vision":"I consent to sending the selected crop to DeepSeek Vision","请先确认 Vision 截图发送同意项。":"Confirm Vision screenshot consent first.","屏显与悬浮":"Overlay & display","输出方式":"Output mode","仅屏显":"Overlay only","仅播报":"Speech only","屏显 + 播报":"Overlay + speech","显示位置":"Display position","自动避让":"Automatic","识别区上方":"Above recognition area","识别区下方":"Below recognition area","识别区左侧":"Left of recognition area","识别区右侧":"Right of recognition area","自定义区域":"Custom region","文字透明度":"Text opacity",
    "玩家聊天语音":"Player chat speech","紧急提醒":"Urgent alerts","重点沟通":"Key messages","中性闲聊":"Neutral chat","系统 Ping 与固定通知":"System pings & fixed notices","击杀播报":"Kill feed","地图资源与建筑":"Map objectives & structures","防沉迷提示":"Wellness reminders","对局流程与系统":"Match flow & system","运行偏好":"Runtime preferences","保留情绪，不朗读脏话":"Keep emotion without reading abuse","仅在画面变化时识别":"Recognize only when the image changes","游戏中保持低占用":"Keep resource use low in game","预览悬浮译文":"Preview overlay","恢复默认设置":"Restore defaults",
    "播报状态":"Speech status","已加入队列":"Queued","消息已过期":"Message expired","译文过长":"Text too long","已过滤攻击":"Abuse filtered","语义未通过":"Evidence check failed","当前策略不播报":"Speech tier off","未开语音":"Speech output off","固定 HUD 已拦截":"Fixed game text blocked","无可读摘要":"Nothing safe to read","翻译质量不确定":"Translation unverified",
    "已加入播报队列，实际播放取决于消息时效和当前设置。":"Queued for speech; playback depends on message freshness and current settings.",
    "本次翻译总计{total}条，已排队{spoken}条":"This session: {total} translations · {spoken} queued","版本号：RiftLens-{version}":"Version: RiftLens {version}"
  };
  const paletteLabels = { porcelain:"瓷白", mono:"黑白", pearl:"纯白", coral:"珊瑚", blue:"雾蓝", teal:"青绿", sage:"鼠尾草", sand:"暖砂", custom:"自定义" };
  const silenceLabels = { expired:"消息已过期", speech_too_long:"译文过长", toxicity:"已过滤攻击", semantic:"语义未通过", policy:"当前策略不播报", overlay_only:"未开语音", hud:"固定 HUD 已拦截", empty:"无可读摘要", translation_quality:"翻译质量不确定" };
  const themedProperties = ["--page","--paper","--surface","--surface-2","--surface-soft","--surface-info","--ink","--ink-2","--ink-3","--accent","--accent-2","--accent-soft","--accent-ink","--card-1","--card-2","--card-3"];
  const state = {
    language:"zh-CN", onboardingCompleted:false, interfacePalette:"porcelain", customThemeColor:"#6f7f91",
    regionReady:false, displayRegionReady:false, ocrReady:false, aiReady:false, isRunning:false, isPaused:false,
    volume:65, speechRate:1, outputModeIndex:2, autoSpeech:true, tacticalSignals:false, killEvents:false,
    objectiveEvents:false, clientReminders:false, otherSystemEvents:false, speakUrgentPlayerChat:true,
    speakImportantPlayerChat:true, speakNeutralPlayerChat:false, keepEmotion:true, changeOnly:true, lowUsage:true,
    floatingButton:true, minimizeAfterStart:true, closeToTray:true, overlayOpacity:68, overlayPlacementIndex:0,
    recognitionMode:0, visionPrivacyConsent:false,
    modelName:"deepseek-v4-flash", appVersion:"2.3.0", history:[], saveTranslationHistory:false, evaluationRecording:false
  };
  let hydrated = false;
  let currentScreen = "welcome";
  let sessionStartedAt = null;
  let toastTimer = 0;
  let bootAdvanceTimer = 0;
  let renderedHistory = null;
  let renderedHistoryLanguage = "";
  const testsDisableAutoAdvance = preview.has("testOnboarding") || preview.has("preview") || preview.has("testLanguage");

  const tr = value => state.language === "en-US" ? (english[value] || value) : value;
  function post(action, payload) {
    if (window.chrome?.webview) window.chrome.webview.postMessage({ action, payload });
    else (window.__riftLensMessages ||= []).push({ action, payload });
  }

  function applyLanguage(language) {
    state.language = language === "en-US" ? "en-US" : "zh-CN";
    document.documentElement.lang = state.language;
    $$('[data-i18n]').forEach(element => { const key = element.dataset.i18n; element.textContent = tr(key); });
    $$('[data-i18n-aria]').forEach(element => element.setAttribute("aria-label", tr(element.dataset.i18nAria)));
    $("#language-button").textContent = state.language === "en-US" ? "ENG/中" : "中/ENG";
    updateDynamicCopy();
  }

  function clearCustomTheme() { themedProperties.forEach(property => document.body.style.removeProperty(property)); }
  function applyPalette(name, customColor = state.customThemeColor) {
    const supported = Object.hasOwn(paletteLabels, name) ? name : "porcelain";
    state.interfacePalette = supported;
    clearCustomTheme();
    document.body.dataset.palette = supported;
    if (supported === "custom") applyCustomPalette(customColor, false);
    $$('.palette-option').forEach(button => button.setAttribute("aria-pressed", String(button.dataset.palette === supported)));
    $("#palette-name").textContent = tr(paletteLabels[supported]);
    $("#custom-theme-color").value = customColor || "#6f7f91";
  }
  function hexToRgb(hex) { const value = hex.replace("#", ""); return [0,2,4].map(index => parseInt(value.slice(index,index+2),16)); }
  function mixHex(from,to,amount) { const a=hexToRgb(from),b=hexToRgb(to); return "#"+a.map((channel,index)=>Math.round(channel+(b[index]-channel)*amount).toString(16).padStart(2,"0")).join(""); }
  function applyCustomPalette(hex, updateState = true) {
    if (!/^#[0-9a-f]{6}$/i.test(hex)) hex = "#6f7f91";
    if (updateState) { state.interfacePalette = "custom"; state.customThemeColor = hex.toLowerCase(); }
    document.body.dataset.palette = "custom";
    const values={"--page":mixHex(hex,"#ffffff",.90),"--paper":mixHex(hex,"#ffffff",.97),"--surface":mixHex(hex,"#ffffff",.79),"--surface-2":mixHex(hex,"#ffffff",.66),"--surface-soft":mixHex(hex,"#ffffff",.91),"--surface-info":mixHex(hex,"#ffffff",.93),"--ink":mixHex(hex,"#000000",.70),"--ink-2":mixHex(hex,"#313131",.55),"--ink-3":mixHex(hex,"#777777",.55),"--accent":hex,"--accent-2":mixHex(hex,"#000000",.20),"--accent-soft":mixHex(hex,"#ffffff",.78),"--accent-ink":mixHex(hex,"#000000",.45),"--card-1":mixHex(hex,"#ffffff",.73),"--card-2":mixHex(hex,"#ffffff",.64),"--card-3":mixHex(hex,"#ffffff",.55)};
    Object.entries(values).forEach(([property,value])=>document.body.style.setProperty(property,value));
    $$('.palette-option').forEach(button=>button.setAttribute("aria-pressed","false"));
    $("#palette-name").textContent=tr("自定义");
  }

  function showScreen(name) {
    currentScreen = name;
    ["welcome","guide","home"].forEach(screenName => {
      const element = $(`#${screenName}-screen`);
      const active = screenName === name;
      element.hidden = !active;
      element.classList.toggle("is-active", active);
    });
    if (name !== "home") closeAllPopovers();
    scheduleBootAdvance(name);
  }
  function scheduleBootAdvance(name) {
    clearTimeout(bootAdvanceTimer);
    if (testsDisableAutoAdvance) return;
    if (name === "welcome") bootAdvanceTimer = setTimeout(leaveWelcome, 2600);
    else if (name === "guide") bootAdvanceTimer = setTimeout(leaveGuide, 3600);
  }
  function leaveWelcome() {
    const screen = $("#welcome-screen");
    if (currentScreen !== "welcome" || screen.classList.contains("is-leaving")) return;
    clearTimeout(bootAdvanceTimer);
    screen.classList.add("is-leaving");
    setTimeout(() => { screen.classList.remove("is-leaving"); showScreen(state.onboardingCompleted ? "home" : "guide"); }, 330);
  }
  function leaveGuide() {
    if(currentScreen!=="guide"||$("#guide-screen").classList.contains("is-leaving"))return;
    clearTimeout(bootAdvanceTimer);
    $("#guide-screen").classList.add("is-leaving");
    setTimeout(()=>{state.onboardingCompleted=true;post("onboardingCompleted",true);$("#guide-screen").classList.remove("is-leaving");showScreen("home")},270);
  }

  function openPopover(element, trigger) {
    element.hidden=false;
    element.dataset.opening="true";
    requestAnimationFrame(()=>{if(element.dataset.opening!=="true"||element.hidden)return;delete element.dataset.opening;element.classList.add("is-open");element.querySelector("button,input,select")?.focus()});
    if (trigger) trigger.setAttribute("aria-expanded","true");
  }
  function closePopover(element, trigger) {
    if (element.hidden) return;
    delete element.dataset.opening;
    element.classList.remove("is-open");
    if (trigger) trigger.setAttribute("aria-expanded","false");
    setTimeout(()=>{ if(!element.classList.contains("is-open")) element.hidden=true; },190);
  }
  function closeAllPopovers() {
    closePopover($("#settings-popover"),$("#settings-button"));
    closePopover($("#advanced-popover"));
    $("#history-menu").hidden=true;
    $("#history-menu-button").setAttribute("aria-expanded","false");
  }
  function showToast(message,isError=false) {
    const toast=$("#toast"); toast.textContent=message; toast.classList.toggle("error",!!isError); toast.classList.add("show");
    clearTimeout(toastTimer); toastTimer=setTimeout(()=>toast.classList.remove("show"),2100);
  }

  function setChecked(id,value){const input=$(`#${id}`);if(input)input.checked=Boolean(value)}
  function setRange(id,value){const input=$(`#${id}`);if(input)input.value=String(value)}
  function setSegments(value){$$('#output-mode button').forEach(button=>button.classList.toggle("active",Number(button.dataset.value)===Number(value)))}
  function formatHour(date){return new Intl.DateTimeFormat(state.language,{year:"numeric",month:"2-digit",day:"2-digit",hour:"numeric",hour12:state.language==="en-US"}).format(date)}
  function timeText(value){const date=new Date(value);return Number.isNaN(date.valueOf())?"--:--":date.toLocaleTimeString(state.language,{hour:"2-digit",minute:"2-digit",hour12:false})}
  function updateDynamicCopy(){
    $("#version-text").textContent=tr("版本号：RiftLens-{version}").replace("{version}",state.appVersion||"2.3.0");
    const visionMode=Number(state.recognitionMode)===1;
    $("#api-model-label").textContent=visionMode?"DeepSeek V4 Vision Exp":(/v4/i.test(state.modelName||"")?"DeepSeek V4":(state.modelName||"DeepSeek"));
    $("#recognition-model-text").textContent=tr(visionMode?"识别：DeepSeek V4 Vision（实验，韩/中/英）":"识别：本地 PP-OCRv5（韩/中/英）");
    $("#recognition-mode").value=String(visionMode?1:0);
    $("#vision-note").hidden=!visionMode;
    $("#vision-consent-row").hidden=!visionMode;
    $("#primary-action").textContent=tr(state.isRunning?(state.isPaused?"继续":"暂停"):"开始翻译");
    $("#save-api").textContent=tr(state.aiReady?"已连接":"保存并连接");
    $("#speech-rate-value").textContent=tr((state.speechRate??1)===0?"标准":state.speechRate<0?"较慢":state.speechRate>2?"较快":"自然");
    $("#palette-name").textContent=tr(paletteLabels[state.interfacePalette]||"瓷白");
    $("#local-save-button").textContent=tr(state.saveTranslationHistory?"停止本地保存":"开启本地保存");
    $("#evaluation-button span").textContent=tr(state.evaluationRecording?"停止并保存评估记录":"开始真实评估记录");
    const runtimeStatus=$("#runtime-status"),statusMessage=state.statusMessage||"";
    if(runtimeStatus.textContent!==statusMessage)runtimeStatus.textContent=statusMessage;
    runtimeStatus.hidden=!statusMessage;
    if(sessionStartedAt)$("#session-time").textContent=formatHour(sessionStartedAt);
    renderHistory();
  }

  function renderHistory(){
    const items=Array.isArray(state.history)?state.history:[];
    if(renderedHistoryLanguage===state.language && renderedHistory?.length===items.length
      && items.every((item,index)=>{
        const previous=renderedHistory[index];
        return item.time===previous.time && item.original===previous.original
          && item.translation===previous.translation && item.spoken===previous.spoken
          && item.silenceReasonCode===previous.silenceReasonCode && item.silenceReason===previous.silenceReason;
      }))return;
    renderedHistory=items.map(({time,original,translation,spoken,silenceReasonCode,silenceReason})=>({time,original,translation,spoken,silenceReasonCode,silenceReason}));
    renderedHistoryLanguage=state.language;
    const tbody=$("#history-rows"); tbody.textContent="";
    const fragment=document.createDocumentFragment();
    items.forEach(item=>{
      const row=document.createElement("tr");
      const speechLabel=tr(item.spoken?"已加入队列":(silenceLabels[item.silenceReasonCode]||"未播报"));
      const values=[timeText(item.time),item.original||"",item.translation||"",speechLabel];
      values.forEach((value,index)=>{
        const cell=document.createElement("td");cell.textContent=value;
        if(index===2)cell.className="translation-text";
        if(index===3){cell.className=item.spoken?"spoken-yes":"spoken-no";cell.title=item.spoken?tr("已加入播报队列，实际播放取决于消息时效和当前设置。"):(silenceLabels[item.silenceReasonCode]?speechLabel:(item.silenceReason||speechLabel))}
        row.append(cell);
      });
      fragment.append(row);
    });
    tbody.append(fragment);
    $("#empty-history").hidden=items.length>0;
    const spoken=items.filter(item=>item.spoken).length;
    $("#history-summary").textContent=tr("本次翻译总计{total}条，已排队{spoken}条").replace("{total}",items.length).replace("{spoken}",spoken);
  }

  function applyState(next){
    const wasRunning=state.isRunning;
    Object.assign(state,next||{});
    applyPalette(state.interfacePalette||"porcelain",state.customThemeColor||"#6f7f91");
    applyLanguage(state.language);
    if(!wasRunning&&state.isRunning)sessionStartedAt=new Date();
    $("#api-quick-status").classList.toggle("is-ready",state.aiReady);
    setRange("voice-volume",state.volume??65);$("#voice-volume-value").textContent=`${state.volume??65}%`;
    setRange("speech-rate",state.speechRate??1);setRange("overlay-opacity",state.overlayOpacity??68);$("#opacity-value").textContent=`${state.overlayOpacity??68}%`;
    $("#overlay-placement").value=String(state.overlayPlacementIndex||0);
    setChecked("auto-speech",state.autoSpeech);setChecked("tactical-signals",state.tacticalSignals);setChecked("kill-events",state.killEvents);setChecked("objective-events",state.objectiveEvents);setChecked("client-reminders",state.clientReminders);setChecked("other-system-events",state.otherSystemEvents);
    setChecked("urgent-player-speech",state.speakUrgentPlayerChat);setChecked("important-player-speech",state.speakImportantPlayerChat);setChecked("neutral-player-speech",state.speakNeutralPlayerChat);setChecked("keep-emotion",state.keepEmotion);setChecked("change-only",state.changeOnly);setChecked("low-usage",state.lowUsage);setChecked("floating-button",state.floatingButton);setChecked("minimize-after-start",state.minimizeAfterStart);setChecked("close-to-tray",state.closeToTray);setChecked("vision-consent",state.visionPrivacyConsent);
    setSegments(state.outputModeIndex);$("#local-save-button").setAttribute("aria-pressed",String(Boolean(state.saveTranslationHistory)));$("#evaluation-button").classList.toggle("active",Boolean(state.evaluationRecording));
    updateDynamicCopy();
    if(!hydrated){hydrated=true;const requested=preview.get("preview");if(preview.get("testOnboarding")==="true")showScreen("welcome");else if(["welcome","guide","home"].includes(requested))showScreen(requested);else if(preview.has("testLanguage"))showScreen("home");else showScreen("welcome");if(requested==="settings"){$("#home-screen").hidden=false;showScreen("home");openPopover($("#settings-popover"),$("#settings-button"))}if(requested==="advanced"){$("#home-screen").hidden=false;showScreen("home");openPopover($("#advanced-popover"))}}
  }

  $("#welcome-panel").addEventListener("click",leaveWelcome);
  $("#guide-panel").addEventListener("click",leaveGuide);
  $$('.drag-region').forEach(element=>{element.addEventListener("mousedown",event=>{if(event.button===0&&!event.target.closest("button"))post("beginWindowDrag")});element.addEventListener("dblclick",event=>{if(!event.target.closest("button"))post("maximize")})});
  $$('[data-action]').forEach(button=>button.addEventListener("click",()=>{const action=button.dataset.action;if(action)post(action)}));
  $("#api-quick-status").addEventListener("click",()=>$("#api-key").focus());
  $("#save-api").addEventListener("click",()=>{const input=$("#api-key"),key=input.value.trim();if(!key){showToast(tr("请输入 API Key 后再保存。"),true);input.focus();return}post("saveApiKey",{apiKey:key});input.value=""});
  $("#language-button").addEventListener("click",()=>{const language=state.language==="en-US"?"zh-CN":"en-US";applyLanguage(language);post("languageChanged",language)});

  function bindToggle(id,action){const input=$(`#${id}`);input?.addEventListener("change",()=>post(action,input.checked))}
  [["auto-speech","toggleAutoSpeech"],["tactical-signals","tacticalSignalsChanged"],["kill-events","killEventsChanged"],["objective-events","objectiveEventsChanged"],["client-reminders","clientRemindersChanged"],["other-system-events","otherSystemEventsChanged"],["urgent-player-speech","urgentPlayerSpeechChanged"],["important-player-speech","importantPlayerSpeechChanged"],["neutral-player-speech","neutralPlayerSpeechChanged"],["keep-emotion","keepEmotionChanged"],["change-only","changeOnlyChanged"],["low-usage","lowUsageChanged"],["floating-button","floatingButtonChanged"],["minimize-after-start","minimizeAfterStartChanged"],["close-to-tray","closeToTrayChanged"],["vision-consent","visionPrivacyConsentChanged"]].forEach(([id,action])=>bindToggle(id,action));
  $("#recognition-mode").addEventListener("change",event=>{state.recognitionMode=Number(event.target.value)===1?1:0;updateDynamicCopy();post("recognitionModeChanged",state.recognitionMode);if(state.recognitionMode===1&&!state.visionPrivacyConsent)showToast(tr("请先确认 Vision 截图发送同意项。"),true)});
  function bindRange(id,action,output,format=value=>`${value}%`){const input=$(`#${id}`),target=$(`#${output}`);input?.addEventListener("input",()=>{if(target)target.textContent=format(Number(input.value))});input?.addEventListener("change",()=>post(action,Number(input.value)))}
  bindRange("voice-volume","volumeChanged","voice-volume-value");bindRange("speech-rate","speechRateChanged","speech-rate-value",value=>tr(value===0?"标准":value<0?"较慢":value>2?"较快":"自然"));bindRange("overlay-opacity","overlayOpacityChanged","opacity-value");
  $$('#output-mode button').forEach(button=>button.addEventListener("click",()=>{state.outputModeIndex=Number(button.dataset.value);setSegments(state.outputModeIndex);post("outputModeChanged",state.outputModeIndex)}));
  $("#overlay-placement").addEventListener("change",event=>post("overlayPlacementChanged",Number(event.target.value)));
  $("#local-save-button").addEventListener("click",()=>{state.saveTranslationHistory=!state.saveTranslationHistory;$("#local-save-button").setAttribute("aria-pressed",String(state.saveTranslationHistory));updateDynamicCopy();post("saveTranslationHistoryChanged",state.saveTranslationHistory)});

  $$('.palette-option').forEach(button=>button.addEventListener("click",()=>{const palette=button.dataset.palette;applyPalette(palette);post("interfacePaletteChanged",palette)}));
  $("#custom-theme-color").addEventListener("input",event=>applyCustomPalette(event.target.value));
  $("#custom-theme-color").addEventListener("change",event=>post("customThemeColorChanged",event.target.value));

  $("#settings-button").addEventListener("click",()=>{const popover=$("#settings-popover"),open=popover.hidden;if(open){closePopover($("#advanced-popover"));openPopover(popover,$("#settings-button"))}else closePopover(popover,$("#settings-button"))});
  $("#more-settings-button").addEventListener("click",()=>{closePopover($("#settings-popover"),$("#settings-button"));openPopover($("#advanced-popover"))});
  $("#advanced-back").addEventListener("click",()=>{closePopover($("#advanced-popover"));openPopover($("#settings-popover"),$("#settings-button"))});
  $("#signal-options").addEventListener("click",()=>{closePopover($("#settings-popover"),$("#settings-button"));openPopover($("#advanced-popover"));setTimeout(()=>$("#system-category-section").scrollIntoView({block:"nearest"}),30)});
  $("#history-menu-button").addEventListener("click",()=>{const menu=$("#history-menu"),open=menu.hidden;menu.hidden=!open;$("#history-menu-button").setAttribute("aria-expanded",String(open))});
  document.addEventListener("click",event=>{
    if(event.target.closest("#settings-button,#settings-popover,#advanced-popover"))return;
    closePopover($("#settings-popover"),$("#settings-button"));
    closePopover($("#advanced-popover"));
  });
  document.addEventListener("keydown",event=>{if(event.key!=="Escape")return;if(!$("#advanced-popover").hidden){closePopover($("#advanced-popover"));$("#settings-button").focus()}else if(!$("#settings-popover").hidden){closePopover($("#settings-popover"),$("#settings-button"));$("#settings-button").focus()}else if(!$("#history-menu").hidden){$("#history-menu").hidden=true;$("#history-menu-button").setAttribute("aria-expanded","false");$("#history-menu-button").focus()}});

  if(window.chrome?.webview){window.chrome.webview.addEventListener("message",event=>{let incoming=event.data;if(typeof incoming==="string"){try{incoming=JSON.parse(incoming)}catch{return}}if(incoming?.type==="state")applyState(incoming.data||{});if(incoming?.type==="toast")showToast(incoming.message,incoming.isError)});post("ready")}
  else {
    const testLanguage=preview.get("testLanguage")||preview.get("language");
    applyState({language:testLanguage==="en-US"?"en-US":"zh-CN",onboardingCompleted:preview.get("firstRun")!=="true",interfacePalette:preview.get("palette")||"porcelain",aiReady:true,ocrReady:true,regionReady:true,displayRegionReady:true,history:[{time:new Date(),original:"[队伍] 레오나: 탑 조심",translation:testLanguage==="en-US"?"Leona: Careful top":"蕾欧娜：上路小心",spoken:true},{time:new Date(),original:"아리: 바텀 강 쪽 조심해",translation:testLanguage==="en-US"?"Ahri: Watch the bottom river":"阿狸：小心下路河道",spoken:true},{time:new Date(),original:"적이 포탑을 파괴했습니다",translation:testLanguage==="en-US"?"The enemy destroyed a turret":"敌方摧毁了一座防御塔",spoken:false}]});
  }
})();
