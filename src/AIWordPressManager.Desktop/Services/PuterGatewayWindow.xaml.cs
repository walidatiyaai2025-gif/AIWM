using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace AIWordPressManager.Desktop.Services;

public partial class PuterGatewayWindow : Window
{
    private const string VirtualHostName = "aiwp-puter.local";
    private readonly CoreWebView2Environment _environment;
    private readonly string _bootstrapFolder;
    private Task? _initialization;

    public PuterGatewayWindow(CoreWebView2Environment environment, string bootstrapFolder)
    {
        _environment = environment;
        _bootstrapFolder = bootstrapFolder;
        InitializeComponent();
        Closing += (_, args) => { args.Cancel = true; Hide(); };
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
        _initialization ??= InitializeAsync(cancellationToken);

    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var value = await ExecuteAsync("window.puterBridge.isSignedIn()", cancellationToken);
        var signedIn = bool.TryParse(value, out var result) && result;
        SetHealth(AuthHealth, "Sign-in", signedIn, signedIn ? "connected" : "required");
        return signedIn;
    }

    public async Task<bool> EnsureSignedInInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        if (!IsVisible) Show();
        Activate();

        if (await IsSignedInAsync(cancellationToken))
        {
            StatusText.Text = "Connected to Puter.";
            return true;
        }

        AppendDiagnostic("Opening Puter sign-in...");
        StatusText.Text = "Complete the Puter sign-in inside this window.";
        await ExecuteAsync("window.puterBridge.signIn()", cancellationToken);
        var signedIn = await IsSignedInAsync(cancellationToken);
        StatusText.Text = signedIn ? "Connected to Puter." : "Puter sign-in was not completed.";
        return signedIn;
    }

    public async Task<string> ChatAsync(string prompt, string model, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var promptJson = JsonSerializer.Serialize(prompt);
        var modelJson = JsonSerializer.Serialize(model);
        return await ExecuteAsync($"window.puterBridge.chat({promptJson}, {modelJson})", cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var json = await ExecuteAsync("window.puterBridge.listModels()", cancellationToken);
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppendDiagnostic("Creating WebView2 environment...");
        await Browser.EnsureCoreWebView2Async(_environment);
        SetHealth(WebViewHealth, "WebView", true, Browser.CoreWebView2.Environment.BrowserVersionString);

        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        Browser.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        Browser.CoreWebView2.ProcessFailed += (_, e) => AppendDiagnostic($"WebView process failure: {e.ProcessFailedKind}");

        Directory.CreateDirectory(_bootstrapFolder);
        var bootstrapPath = Path.Combine(_bootstrapFolder, "puter-bootstrap.html");
        await File.WriteAllTextAsync(bootstrapPath, Html, cancellationToken);
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            _bootstrapFolder,
            CoreWebView2HostResourceAccessKind.DenyCors);

        AppendDiagnostic("Navigating to secure local origin...");
        Browser.CoreWebView2.Navigate($"https://{VirtualHostName}/puter-bootstrap.html");

        var timeout = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var healthJson = await ExecuteAsync("window.puterBridge?.health()", cancellationToken, false);
                if (!string.IsNullOrWhiteSpace(healthJson))
                {
                    using var doc = JsonDocument.Parse(healthJson);
                    var root = doc.RootElement;
                    var cryptoReady = root.TryGetProperty("crypto", out var crypto) && crypto.GetBoolean();
                    var puterReady = root.TryGetProperty("puter", out var puter) && puter.GetBoolean();
                    SetHealth(CryptoHealth, "Crypto", cryptoReady, cryptoReady ? "ready" : "unavailable");
                    SetHealth(PuterHealth, "Puter.js", puterReady, puterReady ? "ready" : "loading");
                    if (puterReady)
                    {
                        StatusText.Text = "Puter.js is ready.";
                        AppendDiagnostic("Gateway ready.");
                        await IsSignedInAsync(cancellationToken);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendDiagnostic($"Waiting: {ex.Message}");
            }
            await Task.Delay(300, cancellationToken);
        }

        throw new InvalidOperationException("Puter.js did not finish loading after 45 seconds. Use Reload, verify internet access, and update Microsoft Edge WebView2 Runtime if necessary.");
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (!string.IsNullOrWhiteSpace(message)) AppendDiagnostic(message);
        }
        catch { }
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var popupBrowser = new Microsoft.Web.WebView2.Wpf.WebView2();
            var popup = new Window
            {
                Title = "Puter Sign In",
                Width = 760,
                Height = 780,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = popupBrowser
            };
            await popupBrowser.EnsureCoreWebView2Async(_environment);
            e.NewWindow = popupBrowser.CoreWebView2;
            popupBrowser.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
            popup.Show();
        }
        finally
        {
            e.Handled = true;
            deferral.Complete();
        }
    }

    private async Task<string> ExecuteAsync(string expression, CancellationToken cancellationToken, bool ensureReady = true)
    {
        if (ensureReady) await EnsureReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await Browser.ExecuteScriptAsync($"(async()=>{{try{{const value=await ({expression});return JSON.stringify({{ok:true,value}});}}catch(error){{return JSON.stringify({{ok:false,error:error?.message||String(error)}});}}}})()");
        var outer = JsonSerializer.Deserialize<string>(raw) ?? raw;
        using var document = JsonDocument.Parse(outer);
        var root = document.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException(root.TryGetProperty("error", out var error) ? error.GetString() : "Unknown Puter.js error.");

        var value = root.GetProperty("value");
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };
    }

    private void AppendDiagnostic(string message)
    {
        Dispatcher.Invoke(() =>
        {
            DiagnosticsText.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            DiagnosticsText.ScrollToEnd();
        });
    }

    private static void SetHealth(System.Windows.Controls.TextBlock target, string name, bool ok, string detail)
    {
        target.Text = $"● {name}: {detail}";
        target.Foreground = ok ? Brushes.ForestGreen : Brushes.DarkGoldenrod;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try { await EnsureSignedInInteractivelyAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; AppendDiagnostic(ex.ToString()); }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _initialization = null;
            DiagnosticsText.Clear();
            await EnsureReadyAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            AppendDiagnostic(ex.ToString());
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private const string Html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<style>
body{font-family:Segoe UI,Arial,sans-serif;margin:0;padding:24px;background:#f7f7f7;color:#1d1d1f}
.card{max-width:720px;margin:auto;background:white;border:1px solid #ddd;border-radius:14px;padding:24px;box-shadow:0 8px 30px rgba(0,0,0,.08)}
h2{color:#765600;margin-top:0}button{background:#765600;color:white;border:0;border-radius:8px;padding:11px 18px;cursor:pointer}button:hover{background:#614700}
#status{margin-top:16px;white-space:pre-wrap}.small{color:#666;font-size:13px}
</style>
<script>
(function(){
  const c=globalThis.crypto||{};
  if(typeof c.randomUUID!=='function'){
    const randomBytes=(size)=>{const a=new Uint8Array(size);if(typeof c.getRandomValues==='function')return c.getRandomValues(a);for(let i=0;i<size;i++)a[i]=Math.floor(Math.random()*256);return a;};
    const uuid=()=>{const b=randomBytes(16);b[6]=(b[6]&15)|64;b[8]=(b[8]&63)|128;const h=[...b].map(x=>x.toString(16).padStart(2,'0')).join('');return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`;};
    try{Object.defineProperty(c,'randomUUID',{value:uuid,configurable:true});}catch{c.randomUUID=uuid;}
    if(!globalThis.crypto)globalThis.crypto=c;
  }
  globalThis.chrome?.webview?.postMessage('Crypto bootstrap ready. randomUUID='+(typeof globalThis.crypto?.randomUUID));
})();
</script>
<script src="https://js.puter.com/v2/"></script>
</head>
<body>
<div class="card">
<h2>Puter AI Connection</h2>
<p>This gateway runs from a secure local HTTPS origin. Sign in once; the WebView2 profile keeps the session.</p>
<button onclick="connect()">Sign in to Puter</button>
<div id="status">Loading Puter.js...</div>
<p class="small">No developer API key is stored. Usage is associated with the signed-in Puter account.</p>
</div>
<script>
const status=document.getElementById('status');
const log=(m)=>{status.textContent=m;globalThis.chrome?.webview?.postMessage(m);};
async function waitForPuter(){for(let i=0;i<180;i++){if(window.puter?.ai&&window.puter?.auth)return true;await new Promise(r=>setTimeout(r,250));}throw new Error('Puter.js failed to load.');}
async function responseText(result){
 if(typeof result==='string')return result;
 if(result?.message?.content){if(typeof result.message.content==='string')return result.message.content;if(Array.isArray(result.message.content))return result.message.content.map(x=>x?.text||'').join('');}
 return result?.text||result?.content||JSON.stringify(result);
}
window.puterBridge={
 ready:false,
 health(){return {crypto:typeof globalThis.crypto?.randomUUID==='function',puter:!!(window.puter?.ai&&window.puter?.auth),secure:window.isSecureContext};},
 async isSignedIn(){await waitForPuter();return !!puter.auth.isSignedIn();},
 async signIn(){await waitForPuter();if(!puter.auth.isSignedIn())await puter.auth.signIn();return !!puter.auth.isSignedIn();},
 async listModels(){await waitForPuter();const models=await puter.ai.listModels();return(models||[]).map(x=>x.id||x.model||x.name).filter(Boolean);},
 async chat(prompt,model){await waitForPuter();if(!puter.auth.isSignedIn())throw new Error('Puter is not signed in.');const result=await puter.ai.chat(prompt,{model:model||'openai/gpt-5-nano',temperature:0.2,max_tokens:2400});return await responseText(result);}
};
waitForPuter().then(()=>{window.puterBridge.ready=true;log(puter.auth.isSignedIn()?'Connected to Puter.':'Ready. Click Sign in to Puter.');}).catch(e=>log(e.message));
async function connect(){try{log('Opening Puter sign-in...');await window.puterBridge.signIn();log('Connected to Puter.');}catch(e){log(e.message);}}
</script>
</body>
</html>
""";
}
