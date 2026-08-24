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
        click("#welcome-continue");
        await delay(380);
        verify(!document.querySelector("#guide-screen").hidden, "First-run guide did not open");
        click("#guide-continue");
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

      document.querySelector("#api-key").value = "test-api-key-placeholder";
      click("#save-api"); expectMessage("saveApiKey", { apiKey: "test-api-key-placeholder" });
      click("#reveal-key"); verify(document.querySelector("#api-key").type === "text", "API key visibility failed");
      click("#primary-action"); expectMessage("togglePrimary");
      click("#local-save-button"); expectMessage("saveTranslationHistoryChanged", true);

      click("#history-menu-button"); verify(!document.querySelector("#history-menu").hidden, "History actions did not open");
      click('#history-menu [data-action="exportDiagnostics"]'); expectMessage("exportDiagnostics");
      click('#history-menu [data-action="toggleEvaluationRecording"]'); expectMessage("toggleEvaluationRecording");

      const otherLanguage = language === "en-US" ? "zh-CN" : "en-US";
      click("#language-button");
      verify(document.documentElement.lang === otherLanguage, "Full language switch failed");
      expectMessage("languageChanged", otherLanguage);
      verify(document.querySelector(".history-card h2").textContent.includes(otherLanguage === "en-US" ? "Translation" : "翻译"), "History heading did not localize");
      click("#language-button"); verify(document.documentElement.lang === language, "Language switch-back failed");

      result.dataset.status = "pass";
      result.textContent = `PASS ${language} interactions`;
    } catch (error) {
      result.dataset.status = "fail";
      result.textContent = `${language}: ${error.message}`;
    }
  }, { once: true });
})();
