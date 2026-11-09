using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RiftLens.Models;
using RiftLens.Services;
using RiftLens.UI;

internal static class MainWindowDiagnostics
{
    public static Task<int> CaptureAsync(string output, int width = 1240, int height = 810, string page = "home")
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                var outputPath = Path.GetFullPath(output);
                var directory = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(directory);
                var store = new SettingsStore(Path.Combine(directory, "capture-settings.json"));
                store.SaveAsync(new AppSettings { OnboardingCompleted = true, CloseToTray = false }).GetAwaiter().GetResult();
                using var form = new MainForm(store, Path.Combine(directory, "webview-profile"));
                Exception? failure = null;
                Task? shutdownProbe = null;
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        // Capture the actual WinForms/WebView2 host, with isolated settings.
                        form.Size = new Size(width, height);
                        var webView = FindWebView(form) ?? throw new InvalidOperationException("WebView2 control missing.");
                        var deadline = DateTime.UtcNow.AddSeconds(20);
                        while (webView.CoreWebView2 is null
                            || await webView.CoreWebView2.ExecuteScriptAsync("document.querySelector('.home-grid') !== null && !document.getElementById('home-screen').hidden") != "true")
                        {
                            if (DateTime.UtcNow > deadline)
                            {
                                var detail = webView.CoreWebView2 is null
                                    ? string.Join(" | ", AllControls(form).Select(control => control.Text).Where(text => text.Length > 0))
                                    : await webView.CoreWebView2.ExecuteScriptAsync("document.body.innerText.slice(0,600)");
                                throw new TimeoutException("Main workspace did not render: " + detail);
                            }
                            await Task.Delay(100);
                        }
                        if (page != "home")
                        {
                            typeof(MainForm).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(form, new object[] { page });
                        }
                        await Task.Delay(1500);
                        await VerifyHiddenUpdatesAsync(form, webView);
                        await using (var stream = File.Create(outputPath))
                        {
                            await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        }
                        var metrics = await webView.CoreWebView2.ExecuteScriptAsync("""
                            JSON.stringify({width:innerWidth,height:innerHeight,dpr:devicePixelRatio,
                              fonts:[...document.fonts].map(f=>({family:f.family,status:f.status})),
                              elements:[...document.querySelectorAll('.app-stage,.workspace,.home-grid,.left-stack,.home-card,.card-heading h2,.system-copy,.brand,th,.quick-button,.empty-history')].map(e=>{
                                const s=getComputedStyle(e),r=e.getBoundingClientRect();
                                return {selector:e.className||e.tagName,text:e.matches('input')?'':e.innerText.slice(0,90),
                                  x:r.x,y:r.y,width:r.width,height:r.height,font:s.fontFamily,size:s.fontSize,weight:s.fontWeight,
                                  color:s.color,background:s.backgroundColor,transform:s.transform,zoom:s.zoom,
                                  scrollHeight:e.scrollHeight,clientHeight:e.clientHeight};})})
                            """);
                        await File.WriteAllTextAsync(Path.ChangeExtension(outputPath, ".json"),
                            JsonSerializer.Deserialize<string>(metrics));
                        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
                        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("CSS.enable", "{}");
                        using var dom = JsonDocument.Parse(await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}"));
                        var rootId = dom.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
                        var fonts = new List<object>();
                        foreach (var selector in new[] { ".brand", ".voice-row", ".system-copy p", ".card-heading h2", ".quick-button.primary" })
                        {
                            using var node = JsonDocument.Parse(await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.querySelector",
                                JsonSerializer.Serialize(new { nodeId = rootId, selector })));
                            var nodeId = node.RootElement.GetProperty("nodeId").GetInt32();
                            var resolved = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("CSS.getPlatformFontsForNode", JsonSerializer.Serialize(new { nodeId }));
                            fonts.Add(new { selector, resolved = JsonSerializer.Deserialize<JsonElement>(resolved) });
                        }
                        await File.WriteAllTextAsync(Path.ChangeExtension(outputPath, ".fonts.json"), JsonSerializer.Serialize(fonts));
                        Console.WriteLine($"Captured native WebView2: {outputPath} | window={form.Size} DPI={form.DeviceDpi} browserPID={webView.CoreWebView2.BrowserProcessId}");
                        // Reproduce the closing deadlock: a worker must resume on this UI
                        // synchronization context after cancellation before disposal can finish.
                        var lifetime = new CancellationTokenSource();
                        var coordinator = new CaptureCoordinator(new AppSettings(), await HeroDictionary.LoadAsync(), new NoNetworkTranslator());
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        typeof(CaptureCoordinator).GetField("_cancellation", flags)!.SetValue(coordinator, lifetime);
                        shutdownProbe = FinishOnUiAsync(lifetime.Token);
                        typeof(CaptureCoordinator).GetField("_translationTask", flags)!.SetValue(coordinator, shutdownProbe);
                        typeof(MainForm).GetField("_coordinator", flags)!.SetValue(form, coordinator);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { form.Close(); }
                };
                Application.Run(form);
                if (failure is not null) throw failure;
                if (shutdownProbe is not { IsCompletedSuccessfully: true })
                    throw new InvalidOperationException("Closing did not await the UI-bound worker.");
                Console.WriteLine("PASS: native close completed with an active UI-bound worker; window disposed.");
                completion.SetResult(0);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static WebView2? FindWebView(Control parent) => parent is WebView2 view ? view
        : parent.Controls.Cast<Control>().Select(FindWebView).FirstOrDefault(view => view is not null);

    private static async Task VerifyHiddenUpdatesAsync(MainForm form, WebView2 webView)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (typeof(MainForm).GetField("_ocrWarmupTask", flags)!.GetValue(form) is Task warmup) await warmup;
        var workspace = typeof(MainForm).GetField("_workspace", flags)!.GetValue(form)!;
        var volume = workspace.GetType().GetProperty("Volume")!;
        var originalVolume = (int)volume.GetValue(workspace)!;
        var push = workspace.GetType().GetMethod("PushState", flags)!;
        await webView.CoreWebView2.ExecuteScriptAsync("window.__perfHostMessages=0; window.chrome.webview.addEventListener('message', e=>{if(e.data?.type==='state')window.__perfHostMessages++});");
        try
        {
            form.Hide();
            if (((Control)workspace).Visible) throw new InvalidOperationException("Workspace remained visible after hiding its form.");
            for (var index = 0; index < 100; index++)
            {
                volume.SetValue(workspace, index);
                push.Invoke(workspace, null);
            }
            form.Show();
            // Script runs after resume and after the latest state reaches the view.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (await webView.CoreWebView2.ExecuteScriptAsync("document.getElementById('voice-volume').value") != "\"99\"")
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Showing WebView did not apply the latest hidden state.");
                await Task.Delay(25);
            }
            var count = await webView.CoreWebView2.ExecuteScriptAsync("window.__perfHostMessages");
            if (count != "1") throw new InvalidOperationException("Expected one state snapshot after 100 hidden updates, received " + count);
            Console.WriteLine("PASS: 100 hidden state updates produce one snapshot on resume, with the latest value.");
        }
        finally
        {
            volume.SetValue(workspace, originalVolume);
            form.Show();
            push.Invoke(workspace, null);
        }
    }

    private static IEnumerable<Control> AllControls(Control parent) => new[] { parent }
        .Concat(parent.Controls.Cast<Control>().SelectMany(AllControls));

    private static async Task FinishOnUiAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.Infinite, token); }
        catch (OperationCanceledException) { await Task.Yield(); }
    }

    private sealed class NoNetworkTranslator : ITranslationService
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Screenshot diagnostics never call a translation service.");
    }
}
