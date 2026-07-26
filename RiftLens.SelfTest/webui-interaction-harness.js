(() => {
  "use strict";

  addEventListener("load", () => {
  const language = new URLSearchParams(location.search).get("testLanguage") || "zh-CN";
  const messages = window.__riftLensMessages || [];
  const result = document.createElement("pre");
  result.id = "interaction-test-result";
  result.dataset.status = "running";
  result.textContent = "RUNNING";
  document.body.append(result);

  function verify(condition, message) {
    if (!condition) throw new Error(message);
  }

  function click(selector) {
    const element = document.querySelector(selector);
    verify(element, `Missing control: ${selector}`);
    element.click();
    return element;
  }

  function last(action) {
    return [...messages].reverse().find(message => message.action === action);
  }

  function expectMessage(action, payload) {
    const item = last(action);
    verify(item, `No message posted for ${action}`);
    if (arguments.length === 2) {
      verify(JSON.stringify(item.payload) === JSON.stringify(payload), `Wrong payload for ${action}`);
    }
  }

  try {
    verify(document.documentElement.lang === language, `Failed to initialize ${language}`);
    for (const target of ["history", "voice", "settings", "home"]) {
      click(`.sidebar [data-nav="${target}"]`);
      verify(document.querySelector(`#page-${target}`).classList.contains("active"), `Navigation failed: ${target}`);
      expectMessage("navigate", target);
    }

    const actions = [...new Set([...document.querySelectorAll("[data-action]")].map(element => element.dataset.action))];
    document.querySelectorAll("[data-action]").forEach(element => element.click());
    actions.forEach(action => verify(messages.some(message => message.action === action), `Action failed: ${action}`));

    click('.sidebar [data-nav="settings"]');
    verify(document.querySelector(".always-state").textContent.trim().length > 0, "Always-on player-chat state is missing");
    verify(document.querySelector(".settings-card:nth-child(2) .card-note").textContent.includes(language === "en-US" ? "Colour" : "颜色"),
      "Structure-and-colour classification note is missing");
    click('#output-mode button[data-value="1"]');
    verify(document.querySelector('#output-mode button[data-value="1"]').classList.contains("active"), "Output mode lacks immediate feedback");
    expectMessage("outputModeChanged", 1);

    const placement = document.querySelector("#overlay-placement");
    placement.value = "4";
    placement.dispatchEvent(new Event("change", { bubbles: true }));
    verify(placement.value === "4", "Display-position select did not change");
    expectMessage("overlayPlacementChanged", 4);

    const toggles = [
      ["auto-speech", "toggleAutoSpeech"],
      ["urgent-player-speech", "urgentPlayerSpeechChanged"],
      ["important-player-speech", "importantPlayerSpeechChanged"],
      ["neutral-player-speech", "neutralPlayerSpeechChanged"],
      ["tactical-signals", "tacticalSignalsChanged"], ["kill-events", "killEventsChanged"],
      ["objective-events", "objectiveEventsChanged"], ["client-reminders", "clientRemindersChanged"],
      ["other-system-events", "otherSystemEventsChanged"],
      ["keep-emotion", "keepEmotionChanged"], ["change-only", "changeOnlyChanged"],
      ["settings-emotion", "keepEmotionChanged"],
      ["floating-button", "floatingButtonChanged"], ["low-usage", "lowUsageChanged"],
      ["minimize-after-start", "minimizeAfterStartChanged"], ["close-to-tray", "closeToTrayChanged"]
    ];
    toggles.forEach(([id, action]) => {
      const input = document.querySelector(`#${id}`);
      input.checked = !input.checked;
      input.dispatchEvent(new Event("change", { bubbles: true }));
      expectMessage(action, input.checked);
    });

    [
      ["global-volume", "volumeChanged", 44], ["voice-volume", "volumeChanged", 47],
      ["overlay-opacity", "overlayOpacityChanged", 62], ["speech-rate", "speechRateChanged", 3]
    ].forEach(([id, action, value]) => {
      const input = document.querySelector(`#${id}`);
      input.value = String(value);
      input.dispatchEvent(new Event("input", { bubbles: true }));
      input.dispatchEvent(new Event("change", { bubbles: true }));
      expectMessage(action, value);
    });

    click('.sidebar [data-nav="voice"]');
    const help = click(".help");
    verify(help.getAttribute("aria-expanded") === "true", "Help button did not expand");
    verify(document.querySelector("#tooltip").classList.contains("show"), "Help tooltip did not appear");
    help.click();
    verify(help.getAttribute("aria-expanded") === "false", "Help button did not collapse");

    click('.sidebar [data-nav="home"]');
    document.querySelector("#api-key").value = "test-api-key-placeholder";
    click("#save-api");
    expectMessage("saveApiKey", { apiKey: "test-api-key-placeholder" });
    click("#reveal-key");
    verify(document.querySelector("#api-key").type === "text", "API key visibility button failed");

    click('.sidebar [data-nav="settings"]');
    const honestReadOnlyStates = [...document.querySelectorAll(".static-value, .unavailable-note, .bundled-state, .fixed-label")]
      .every(element => !["BUTTON", "INPUT", "SELECT"].includes(element.tagName) && !element.hasAttribute("tabindex"));
    verify(honestReadOnlyStates, "A fixed capability still looks operable");

    const otherLanguage = language === "en-US" ? "zh-CN" : "en-US";
    click(`[data-language="${otherLanguage}"]`);
    verify(document.documentElement.lang === otherLanguage, "Language switch failed");
    expectMessage("languageChanged", otherLanguage);
    click(`[data-language="${language}"]`);
    verify(document.documentElement.lang === language, "Language switch-back failed");

    result.dataset.status = "pass";
    result.textContent = `PASS ${language} interactions`;
  } catch (error) {
    result.dataset.status = "fail";
    result.textContent = `${language}: ${error.message}`;
  }
  }, { once: true });
})();
