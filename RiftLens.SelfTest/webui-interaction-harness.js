(() => {
  "use strict";

  addEventListener("load", async () => {
    const params = new URLSearchParams(location.search);
    const language = params.get("testLanguage") || "zh-CN";
    const onboardingMode = params.get("testOnboarding") === "true";
    const messages = window.__riftLensMessages || [];
    const result = document.createElement("pre");
    result.id = "interaction-test-result";
    result.dataset.status = "running";
    result.textContent = "RUNNING";
    document.body.append(result);

    const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
    async function waitUntil(predicate, message) {
      const deadline=performance.now()+1500;
      while(!predicate()&&performance.now()<deadline)await delay(16);
      verify(predicate(),message);
    }
    function verify(condition, message) { if (!condition) throw new Error(message); }
    function click(selector) { const element = document.querySelector(selector); verify(element, `Missing control: ${selector}`); element.click(); return element; }
    function last(action) { return [...messages].reverse().find(message => message.action === action); }
    function expectMessage(action, payload) {
      const item = last(action); verify(item, `No message posted for ${action}`);
      if (arguments.length === 2) verify(JSON.stringify(item.payload) === JSON.stringify(payload), `Wrong payload for ${action}`);
    }
    function changeToggle(id, action) {
      const input = document.querySelector(`#${id}`); verify(input, `Missing toggle: ${id}`);
      input.checked = !input.checked; input.dispatchEvent(new Event("change", { bubbles: true })); expectMessage(action, input.checked);
    }

    try {
      if (onboardingMode) {
        verify(!document.querySelector("#welcome-screen").hidden, "Welcome screen did not open");
        click("#welcome-panel");
        await delay(380);
        verify(!document.querySelector("#guide-screen").hidden, "First-run guide did not open");
        click("#guide-panel");
        await delay(310);
        verify(!document.querySelector("#home-screen").hidden, "Guide did not continue to Home");
        expectMessage("onboardingCompleted", true);
        result.dataset.status = "pass";
        result.textContent = `PASS onboarding flow`;
        return;
      }

      verify(document.documentElement.lang === language, `Failed to initialize ${language}`);
      verify(!document.querySelector("#home-screen").hidden, "Home screen is not visible");
      verify(document.querySelectorAll("#history-rows tr").length === 2, "History rows were not rendered");

      const homeScreenStyle = getComputedStyle(document.querySelector(".home-screen"));
      const homeShellStyle = getComputedStyle(document.querySelector(".home-shell"));
      const leftStackStyle = getComputedStyle(document.querySelector(".left-stack"));
      verify(homeScreenStyle.paddingTop === "0px", "Home still has an outer presentation layer");
      verify(homeShellStyle.borderTopLeftRadius === "0px", "Home shell still has a nested outer radius");
      verify(leftStackStyle.display === "flex", "Left cards are not using content-driven sizing");
      const clippedCard = [...document.querySelectorAll(".left-stack > .home-card")]
        .find(card => card.scrollHeight > card.clientHeight + 1);
      verify(!clippedCard, `Left card content is clipped in ${language}`);

      click("#settings-button");
      verify(!document.querySelector("#settings-popover").hidden, "Settings popover did not open");
      click(".history-card");
      await delay(210);
      verify(document.querySelector("#settings-popover").hidden, "Settings popover did not close after an outside click");
      verify(document.querySelector("#settings-button").getAttribute("aria-expanded") === "false", "Settings trigger stayed expanded after an outside click");
      click("#settings-button");
      verify(!document.querySelector("#settings-popover").hidden, "Settings popover did not reopen");
      click('[data-palette="mono"]');
      expectMessage("interfacePaletteChanged", "mono");
      verify(document.body.dataset.palette === "mono", "Palette did not update immediately");

      const custom = document.querySelector("#custom-theme-color");
      custom.value = "#345678";
      custom.dispatchEvent(new Event("input", { bubbles: true }));
      custom.dispatchEvent(new Event("change", { bubbles: true }));
      expectMessage("customThemeColorChanged", "#345678");
      verify(document.body.dataset.palette === "custom", "Custom palette did not update card surfaces");

      [["minimize-after-start","minimizeAfterStartChanged"],["close-to-tray","closeToTrayChanged"],["floating-button","floatingButtonChanged"]]
        .forEach(([id, action]) => changeToggle(id, action));

      click("#more-settings-button");
      verify(!document.querySelector("#advanced-popover").hidden, "Advanced settings did not open");
      const recognitionMode = document.querySelector("#recognition-mode");
      recognitionMode.value = "1";
      recognitionMode.dispatchEvent(new Event("change", { bubbles: true }));
      expectMessage("recognitionModeChanged", 1);
      verify(!document.querySelector("#vision-note").hidden, "Vision privacy notice did not appear");
      verify(!document.querySelector("#vision-consent-row").hidden, "Vision consent control did not appear");
      changeToggle("vision-consent", "visionPrivacyConsentChanged");
      click('#output-mode button[data-value="1"]'); expectMessage("outputModeChanged", 1);
      const placement = document.querySelector("#overlay-placement"); placement.value = "4"; placement.dispatchEvent(new Event("change", { bubbles: true })); expectMessage("overlayPlacementChanged", 4);
      const opacity = document.querySelector("#overlay-opacity"); opacity.value = "62"; opacity.dispatchEvent(new Event("input", { bubbles: true })); opacity.dispatchEvent(new Event("change", { bubbles: true })); expectMessage("overlayOpacityChanged", 62);
      [["urgent-player-speech","urgentPlayerSpeechChanged"],["important-player-speech","importantPlayerSpeechChanged"],["neutral-player-speech","neutralPlayerSpeechChanged"],["kill-events","killEventsChanged"],["objective-events","objectiveEventsChanged"],["client-reminders","clientRemindersChanged"],["other-system-events","otherSystemEventsChanged"],["keep-emotion","keepEmotionChanged"],["change-only","changeOnlyChanged"],["low-usage","lowUsageChanged"]]
        .forEach(([id, action]) => changeToggle(id, action));

      click(".history-card");
      await delay(210);
      verify(document.querySelector("#advanced-popover").hidden, "Advanced settings did not close after an outside click");

      changeToggle("auto-speech", "toggleAutoSpeech");
      changeToggle("tactical-signals", "tacticalSignalsChanged");
      const volume = document.querySelector("#voice-volume"); volume.value = "44"; volume.dispatchEvent(new Event("input", { bubbles: true })); volume.dispatchEvent(new Event("change", { bubbles: true })); expectMessage("volumeChanged", 44);
      const rate = document.querySelector("#speech-rate"); rate.value = "3"; rate.dispatchEvent(new Event("input", { bubbles: true })); rate.dispatchEvent(new Event("change", { bubbles: true })); expectMessage("speechRateChanged", 3);

      const apiKeyInput = document.querySelector("#api-key");
      verify(apiKeyInput.type === "password", "API key input is not permanently masked");
      verify(!document.querySelector("#reveal-key"), "API key reveal control still exists");
      document.querySelector("#api-form").requestSubmit();
      verify(apiKeyInput.getAttribute("aria-invalid")==="true"
        && document.querySelector("#api-feedback").classList.contains("is-error")
        && !last("saveApiKey"), "An empty API key was submitted or lacked inline feedback");
      apiKeyInput.value = "test-api-key-placeholder";
      apiKeyInput.dispatchEvent(new Event("input", {bubbles:true}));
      click("#save-api"); expectMessage("saveApiKey", { apiKey: "test-api-key-placeholder" });
      verify(apiKeyInput.type === "password", "API key input became visible after saving");
      verify(apiKeyInput.value === "", "API key remained in the WebUI field after saving");
      verify(document.querySelector("#save-api").disabled
        && document.querySelector("#save-api").getAttribute("aria-busy")==="true", "Saving has no pending state");
      document.querySelector("#api-form").requestSubmit();
      verify(messages.filter(message=>message.action==="saveApiKey").length===1, "A pending API save was submitted twice");
      const saveError=language==="en-US"?"The key could not be saved.":"密钥未能保存。";
      window.__riftLensHostMessage({data:{type:"state",data:{apiKeySavePending:false,apiKeySaveError:saveError}}});
      verify(!document.querySelector("#save-api").disabled
        && document.querySelector("#api-feedback").textContent===saveError, "Host save errors were not shown next to the input");
      apiKeyInput.dispatchEvent(new Event("input",{bubbles:true}));
      verify(apiKeyInput.getAttribute("aria-invalid")==="false", "Correcting the input did not clear validation");
      click("#primary-action"); expectMessage("togglePrimary");
      click("#primary-action");
      verify(messages.filter(message=>message.action==="togglePrimary").length===1
        && document.querySelector("#primary-action").disabled, "Start actions were not protected from repeated clicks");
      window.__riftLensHostMessage({data:{type:"state",data:{primaryActionPending:false,apiKeySaveError:""}}});
      verify(!document.querySelector("#primary-action").disabled, "Start action did not recover after host completion");
      click("#local-save-button"); expectMessage("saveTranslationHistoryChanged", true);

      click("#history-menu-button"); verify(!document.querySelector("#history-menu").hidden, "History actions did not open");
      click('#history-menu [data-action="exportDiagnostics"]'); expectMessage("exportDiagnostics");
      verify(document.querySelector("#history-menu").hidden, "History actions did not close after selecting an item");
      click("#history-menu-button");
      click('#history-menu [data-action="toggleEvaluationRecording"]'); expectMessage("toggleEvaluationRecording");
      click("#history-menu-button");click(".history-card");
      verify(document.querySelector("#history-menu").hidden, "History actions did not close after an outside click");
      click("#history-menu-button");click('#history-menu [data-action="clearHistory"]');
      verify(document.querySelector("#confirm-dialog").open&&!last("clearHistory"), "History cleared without explicit confirmation");
      click('#confirm-dialog button[value="cancel"]');
      await waitUntil(()=>!document.querySelector("#confirm-dialog").open
        &&document.activeElement===document.querySelector("#history-menu-button"), "Cancel did not return focus");
      verify(!last("clearHistory")&&document.activeElement===document.querySelector("#history-menu-button"),
        "Cancel cleared history or failed to restore focus");
      click("#history-menu-button");click('#history-menu [data-action="clearHistory"]');
      click('#confirm-dialog button[value="confirm"]');
      await waitUntil(()=>!!last("clearHistory"),"Confirmation did not submit the clear action");expectMessage("clearHistory");
      click("#signal-options");
      await waitUntil(()=>document.querySelector("#advanced-popover").classList.contains("is-open"),"Advanced settings did not finish opening");
      document.dispatchEvent(new KeyboardEvent("keydown",{key:"Escape",bubbles:true}));await delay(210);
      verify(document.querySelector("#advanced-popover").hidden
        &&document.activeElement===document.querySelector("#signal-options"), "Escape did not restore the advanced-settings trigger");
      verify(parseFloat(getComputedStyle(document.querySelector("#voice-volume")).height)>=24, "Range input hit area is too small");
      verify(document.querySelector('#output-mode button.active').getAttribute("aria-pressed")==="true",
        "Output mode selection has no accessible pressed state");

      const otherLanguage = language === "en-US" ? "zh-CN" : "en-US";
      click("#language-button");
      verify(document.documentElement.lang === otherLanguage, "Full language switch failed");
      expectMessage("languageChanged", otherLanguage);
      verify(document.querySelector(".history-card h2").textContent.includes(otherLanguage === "en-US" ? "Translation" : "翻译"), "History heading did not localize");
      click("#language-button"); verify(document.documentElement.lang === language, "Language switch-back failed");

      const deliveryHostState = data => window.__riftLensHostMessage({data:{type:"state",data}});
      const statusMessage = language === "en-US"
        ? "The translation was incomplete; this turn was skipped."
        : "翻译未完整结束，已跳过本轮。";
      const deliveryHistory = [
        {time:"2026-11-09T12:00:00+08:00",original:"first",translation:"第一行\n第二行",spoken:true},
        {time:"2026-11-09T12:00:01+08:00",original:"expired",translation:"过期译文",spoken:false,silenceReasonCode:"expired"},
        {time:"2026-11-09T12:00:02+08:00",original:"long",translation:"保留完整长译文",spoken:false,silenceReasonCode:"speech_too_long"},
        {time:"2026-11-09T12:00:03+08:00",original:"partial",translation:"待确认译文",spoken:false,silenceReasonCode:"translation_quality"}
      ];
      deliveryHostState({statusMessage,history:deliveryHistory});
      const deliveryCells = () => [...document.querySelectorAll("#history-rows tr")].map(row=>row.cells[3].textContent);
      const expectedDelivery = language === "en-US"
        ? ["Queued","Message expired","Text too long","Translation unverified"]
        : ["已加入队列","消息已过期","译文过长","翻译质量不确定"];
      verify(JSON.stringify(deliveryCells())===JSON.stringify(expectedDelivery), "Delivery status or silence reasons were not localized");
      verify(document.querySelector("#runtime-status").textContent===statusMessage
        && !document.querySelector("#runtime-status").hidden
        && document.querySelector("#runtime-status").getAttribute("role")==="status",
        "Backend rejection was not visible as accessible status feedback");
      verify(document.querySelector("#history-summary").textContent.includes(language==="en-US"?"1 queued":"已排队1条"),
        "Queue count was presented as confirmed playback");
      const translationCell = document.querySelector("#history-rows tr").cells[2];
      verify(translationCell.textContent==="第一行\n第二行" && getComputedStyle(translationCell).whiteSpace==="pre-line",
        "Full translation lines were collapsed or truncated");
      deliveryHistory[1].silenceReasonCode="policy";
      deliveryHostState({history:deliveryHistory});
      verify(deliveryCells()[1]===(language==="en-US"?"Speech tier off":"当前策略不播报"),
        "A reason-only history update was lost by the render cache");
      click("#language-button");
      verify(deliveryCells()[2]===(language==="en-US"?"译文过长":"Text too long"),
        "Changing language did not retranslate delivery reasons");
      click("#language-button");
      deliveryHistory[1].silenceReasonCode="future_reason";
      deliveryHostState({history:deliveryHistory,statusMessage:""});
      verify(deliveryCells()[1]===(language==="en-US"?"Silent":"未播报")
        && document.querySelector("#runtime-status").hidden,
        "Unknown reason or cleared status did not fall back safely");

      const historyForPerformance = Array.from({length:150},(_,index)=>({
        time:"2026-09-05T12:00:00+08:00", original:`sample ${index}`, translation:`译文 ${index}`, spoken:false
      }));
      const hostState = data => window.__riftLensHostMessage({data:{type:"state",data}});
      hostState({history:historyForPerformance});
      const historyBody = document.querySelector("#history-rows");
      const originalFirstRow = historyBody.firstElementChild;
      const observer = new MutationObserver(()=>{});
      observer.observe(historyBody,{childList:true,subtree:true});
      for(let index=0;index<100;index++)hostState({volume:index,history:historyForPerformance.map(item=>({...item}))});
      const unchangedHistoryMutations = observer.takeRecords().length;
      observer.disconnect();
      verify(unchangedHistoryMutations===0 && historyBody.firstElementChild===originalFirstRow,
        "Unrelated state updates rebuilt unchanged history rows");
      historyForPerformance[0].translation="更新后的译文";
      hostState({history:historyForPerformance});
      verify(historyBody.firstElementChild.cells[2].textContent==="更新后的译文", "Same-length history edit was missed");
      const languageBefore = document.documentElement.lang;
      click("#language-button");
      verify(historyBody.firstElementChild.cells[3].textContent === (languageBefore==="zh-CN"?"Silent":"未播报"),
        "Cached history did not update after language switch");
      click("#language-button");
      hostState({history:[]});
      verify(historyBody.children.length===0 && !document.querySelector("#empty-history").hidden,
        "Cached history did not clear");
      result.dataset.status = "pass";
      result.textContent = `PASS ${language} interactions; 100 updates / 150 rows: ${unchangedHistoryMutations} history mutations`;
    } catch (error) {
      result.dataset.status = "fail";
      result.textContent = `${language}: ${error.message}`;
    }
  }, { once: true });
})();
