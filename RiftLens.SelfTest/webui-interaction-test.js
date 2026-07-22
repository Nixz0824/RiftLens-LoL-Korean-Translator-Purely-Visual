"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { pathToFileURL } = require("url");
const { spawnSync } = require("child_process");

const browserPath = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const sourcePath = path.resolve(__dirname, "..", "RiftLens", "WebUI", "index.html");
const webUiRoot = path.dirname(sourcePath);
const harnessPath = path.join(__dirname, "webui-interaction-harness.js");

function removeTemporaryItem(target, prefix) {
  const tempRoot = path.resolve(os.tmpdir());
  const resolved = path.resolve(target);
  if (!resolved.startsWith(tempRoot + path.sep) || !path.basename(resolved).startsWith(prefix)) return;
  for (let attempt = 0; attempt < 10; attempt++) {
    try {
      fs.rmSync(resolved, { recursive: true, force: true });
      return;
    } catch {}
  }
}

function runLanguage(language) {
  const suffix = `${process.pid}-${language.replace(/[^a-z]/gi, "")}`;
  const profile = path.join(os.tmpdir(), `riftlens-webui-profile-${suffix}`);
  const testPage = path.join(os.tmpdir(), `riftlens-webui-page-${suffix}.html`);
  fs.mkdirSync(profile, { recursive: true });

  const baseUrl = pathToFileURL(webUiRoot + path.sep).href;
  const harnessUrl = pathToFileURL(harnessPath).href;
  const webViewStub = `<script>
    window.__riftLensMessages = [];
    window.chrome = window.chrome || {};
    Object.defineProperty(window.chrome, "webview", {
      configurable: true,
      value: {
        postMessage(message) { window.__riftLensMessages.push(message); },
        addEventListener(type, handler) { if (type === "message") window.__riftLensHostMessage = handler; }
      }
    });
  <\/script>`;
  const initialState = `<script>
    if (window.__riftLensHostMessage) {
      window.__riftLensHostMessage({ data: { type: "state", data: { page: "home" } } });
    }
  <\/script>`;
  const source = fs.readFileSync(sourcePath, "utf8")
    .replace("<head>", `<head><base href="${baseUrl}">`)
    .replace('<script src="app.js"></script>', `${webViewStub}<script src="app.js"></script>${initialState}<script src="${harnessUrl}"></script>`);
  fs.writeFileSync(testPage, source, "utf8");

  const result = spawnSync(browserPath, [
    "--headless=new",
    "--disable-gpu",
    "--disable-gpu-sandbox",
    "--no-sandbox",
    "--disable-dev-shm-usage",
    "--no-first-run",
    "--no-default-browser-check",
    "--allow-file-access-from-files",
    `--user-data-dir=${profile}`,
    "--dump-dom",
    `${pathToFileURL(testPage).href}?language=${language}&testLanguage=${language}`
  ], { encoding: "utf8", timeout: 30000 });

  removeTemporaryItem(testPage, "riftlens-webui-page-");
  removeTemporaryItem(profile, "riftlens-webui-profile-");

  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr || `Edge exited with code ${result.status}`);
  const status = /<pre id="interaction-test-result" data-status="([^"]+)">([^<]*)<\/pre>/.exec(result.stdout);
  if (!status) throw new Error(`${language}: the interaction harness did not return a result`);
  if (status[1] !== "pass") throw new Error(status[2]);
  return status[2];
}

try {
  const results = [runLanguage("zh-CN"), runLanguage("en-US")];
  console.log(results.join(" | "));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
