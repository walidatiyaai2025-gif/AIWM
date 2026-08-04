using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop.Views;

public partial class VisualWordPressEditorView : UserControl
{
    private bool _initialized;
    private VisualWordPressEditorViewModel? _attachedViewModel;
    private bool _verifyAfterReload;

    public VisualWordPressEditorView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            AttachViewModelEvents();
            await EnsureBrowserAsync();
        };
        DataContextChanged += (_, _) => AttachViewModelEvents();
        Unloaded += (_, _) => DetachViewModelEvents();
    }

    private VisualWordPressEditorViewModel? ViewModel => DataContext as VisualWordPressEditorViewModel;


    private void AttachViewModelEvents()
    {
        var current = ViewModel;
        if (ReferenceEquals(_attachedViewModel, current)) return;

        DetachViewModelEvents();
        if (current is null) return;

        current.VisualCssApplied += ViewModel_VisualCssApplied;
        _attachedViewModel = current;
    }

    private void DetachViewModelEvents()
    {
        if (_attachedViewModel is null) return;
        _attachedViewModel.VisualCssApplied -= ViewModel_VisualCssApplied;
        _attachedViewModel = null;
    }

    private void ViewModel_VisualCssApplied(object? sender, EventArgs e)
    {
        if (!_initialized || ViewModel is null) return;
        _verifyAfterReload = true;
        Browser.CoreWebView2.Navigate(ViewModel.PageUrl);
    }

    private async Task VerifyAppliedCssAsync()
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(ViewModel.SelectedSelector)) return;

        var selectorJson = JsonSerializer.Serialize(ViewModel.SelectedSelector);
        var cssJson = JsonSerializer.Serialize(ViewModel.PreviewCss);
        var script = $$"""
(() => {
 const selector = {{selectorJson}};
 const declarations = {{cssJson}};
 const target = document.querySelector(selector);
 if (!target) return JSON.stringify({ found:false, verified:false, details:'Selector was not found after reload.', checks:[] });
 const probe = document.createElement('div');
 probe.style.cssText = declarations;
 const expected = [];
 for (let i = 0; i < probe.style.length; i++) {
   const property = probe.style[i];
   expected.push({ property, value: probe.style.getPropertyValue(property).trim() });
 }
 if (expected.length === 0) return JSON.stringify({ found:true, verified:false, details:'No valid CSS declarations were supplied.', checks:[] });
 const computed = getComputedStyle(target);
 const checks = expected.map(item => {
   const actual = computed.getPropertyValue(item.property).trim();
   return { property:item.property, expected:item.value, actual, match: actual === item.value || actual.includes(item.value) || item.value.includes(actual) };
 });
 return JSON.stringify({ found:true, verified:checks.every(x => x.match), checks });
})()
""";

        var raw = await Browser.ExecuteScriptAsync(script);
        var json = JsonSerializer.Deserialize<string>(raw) ?? "{}";
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var verified = root.TryGetProperty("verified", out var property) && property.ValueKind == JsonValueKind.True;
        var details = BuildVerificationDetails(root, verified);
        ViewModel.SetVerificationResult(verified, details);

        if (verified)
        {
            await CaptureAsync(before: false);
        }
    }

    private static string BuildVerificationDetails(JsonElement root, bool verified)
    {
        if (verified)
        {
            return "The public page reloaded and every declared CSS property matched the computed style.";
        }

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(detailsElement.GetString()))
        {
            return detailsElement.GetString()!;
        }

        if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
        {
            return "The page reloaded, but CSS verification did not return any property checks. Review the API log or use Rollback.";
        }

        var mismatches = new List<string>();
        foreach (var check in checks.EnumerateArray())
        {
            var matched = check.TryGetProperty("match", out var matchElement) && matchElement.ValueKind == JsonValueKind.True;
            if (matched) continue;

            var propertyName = check.TryGetProperty("property", out var propertyElement) ? propertyElement.GetString() : "property";
            var expected = check.TryGetProperty("expected", out var expectedElement) ? expectedElement.GetString() : string.Empty;
            var actual = check.TryGetProperty("actual", out var actualElement) ? actualElement.GetString() : string.Empty;
            mismatches.Add($"{propertyName}: expected '{expected}', received '{actual}'");
        }

        return mismatches.Count == 0
            ? "The page reloaded, but the visual result could not be verified. Review the WordPress response log or use Rollback."
            : "CSS verification failed for: " + string.Join("; ", mismatches.Take(8));
    }

    private async Task EnsureBrowserAsync()
    {
        if (_initialized) return;
        try
        {
            await Browser.EnsureCoreWebView2Async();
            _initialized = true;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;
            Browser.CoreWebView2.NavigationStarting += (_, _) =>
            {
                if (ViewModel is null) return;
                ViewModel.IsBrowserReady = false;
                ViewModel.IsInspectionEnabled = false;
                ViewModel.StatusMessage = "Loading the public page…";
            };
            Browser.CoreWebView2.NavigationCompleted += async (_, args) =>
            {
                if (ViewModel is null) return;
                ViewModel.IsBrowserReady = args.IsSuccess;
                if (!args.IsSuccess)
                {
                    ViewModel.StatusMessage = $"Page load failed: {args.WebErrorStatus}.";
                    return;
                }

                if (_verifyAfterReload)
                {
                    _verifyAfterReload = false;
                    await VerifyAppliedCssAsync();
                    return;
                }

                ViewModel.StatusMessage = "Page loaded. Click Inspect element, then choose the exact page element to preview.";
            };
        }
        catch (Exception ex)
        {
            if (ViewModel is not null) ViewModel.StatusMessage = "WebView2 could not start: " + ex.Message;
        }
    }

    private async void LoadPage_Click(object sender, RoutedEventArgs e)
    {
        await EnsureBrowserAsync();
        if (!_initialized || ViewModel is null) return;
        if (!Uri.TryCreate(ViewModel.PageUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ViewModel.StatusMessage = "Enter a valid HTTP or HTTPS page URL.";
            return;
        }

        ViewModel.StatusMessage = "Loading the live website in an isolated preview…";
        Browser.CoreWebView2.Navigate(uri.ToString());
    }

    private async void ToggleInspect_Click(object sender, RoutedEventArgs e)
    {
        await EnsureBrowserAsync();
        if (!_initialized || ViewModel is null) return;
        ViewModel.IsInspectionEnabled = !ViewModel.IsInspectionEnabled;
        await Browser.ExecuteScriptAsync(ViewModel.IsInspectionEnabled ? InspectionScript : DisableInspectionScript);
        ViewModel.StatusMessage = ViewModel.IsInspectionEnabled
            ? "Inspection is active. Hover the page and click the element you want to change."
            : "Inspection is paused.";
    }

    private async void ApplyPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || ViewModel is null || !ViewModel.HasSelection)
        {
            if (ViewModel is not null) ViewModel.StatusMessage = "Select an element before applying a local preview.";
            return;
        }

        var selectorJson = JsonSerializer.Serialize(ViewModel.SelectedSelector);
        var cssJson = JsonSerializer.Serialize(ViewModel.PreviewCss);
        var script = $$"""
(() => {
 const selector = {{selectorJson}};
 const css = {{cssJson}};
 let style = document.getElementById('aiwp-visual-preview-style');
 if (!style) {
   style = document.createElement('style');
   style.id = 'aiwp-visual-preview-style';
   document.head.appendChild(style);
 }
 style.textContent = selector + '{' + css + '}';
 const target = document.querySelector(selector);
 if (target) {
   target.scrollIntoView({ behavior:'smooth', block:'center' });
   target.dataset.aiwpPreview = 'true';
 }
 return Boolean(target);
})()
""";
        var result = await Browser.ExecuteScriptAsync(script);
        ViewModel.StatusMessage = result.Contains("true", StringComparison.OrdinalIgnoreCase)
            ? "Local visual preview applied. Capture the after image, then prepare the execution proposal."
            : "The selected element could not be found after the page changed. Inspect it again.";
    }

    private async void ResetPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || ViewModel is null) return;
        await Browser.ExecuteScriptAsync("document.getElementById('aiwp-visual-preview-style')?.remove(); document.querySelectorAll('[data-aiwp-preview]').forEach(x=>x.removeAttribute('data-aiwp-preview')); true;");
        ViewModel.StatusMessage = "Local CSS preview removed. WordPress was never changed.";
    }

    private async void CaptureBefore_Click(object sender, RoutedEventArgs e) => await CaptureAsync(before: true);
    private async void CaptureAfter_Click(object sender, RoutedEventArgs e) => await CaptureAsync(before: false);

    private async Task CaptureAsync(bool before)
    {
        await EnsureBrowserAsync();
        if (!_initialized || ViewModel is null) return;
        var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{(before ? "before" : "after")}.png";
        var path = Path.Combine(ViewModel.GetEvidenceDirectory(), fileName);
        await using var stream = File.Create(path);
        await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        if (before) ViewModel.SetBeforeScreenshot(path); else ViewModel.SetAfterScreenshot(path);
        ViewModel.StatusMessage = $"{(before ? "Before" : "After")} screenshot captured: {path}";
    }

    private async void PrepareProposal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.HasSelection)
        {
            if (ViewModel is not null) ViewModel.StatusMessage = "Select an element before preparing an execution proposal.";
            return;
        }
        await ViewModel.SaveProposalAsync();
        ViewModel.StatusMessage = "Proposal saved locally. It is ready for a future approved Visual CSS Executor adapter; no WordPress write occurred.";
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (ViewModel is null) return;
        try
        {
            var message = JsonSerializer.Deserialize<ElementSelectionMessage>(e.WebMessageAsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (message?.Type == "aiwp-element-selected") ViewModel.SetSelection(message);
        }
        catch (Exception ex)
        {
            ViewModel.LastConsoleMessage = "Could not read the selected element: " + ex.Message;
        }
    }

    private const string DisableInspectionScript = """
(() => {
 window.__aiwpInspectEnabled = false;
 document.getElementById('aiwp-inspector-style')?.remove();
 document.querySelectorAll('[data-aiwp-hover]').forEach(x=>x.removeAttribute('data-aiwp-hover'));
 true;
})()
""";

    private const string InspectionScript = """
(() => {
 if (window.__aiwpInspectorInstalled) { window.__aiwpInspectEnabled = true; return true; }
 window.__aiwpInspectorInstalled = true;
 window.__aiwpInspectEnabled = true;
 const style = document.createElement('style');
 style.id='aiwp-inspector-style';
 style.textContent='[data-aiwp-hover]{outline:3px solid #00a7d6 !important;outline-offset:2px !important;cursor:crosshair !important}[data-aiwp-preview]{outline:3px dashed #6d4aff !important;outline-offset:3px !important}';
 document.head.appendChild(style);
 let hovered = null;
 const selectorFor = el => {
   if (el.id) return '#' + CSS.escape(el.id);
   const parts=[];
   let node=el;
   while(node && node.nodeType===1 && node!==document.body){
     let part=node.tagName.toLowerCase();
     const classes=[...node.classList].filter(x=>!x.startsWith('aiwp')).slice(0,2);
     if(classes.length) part += '.' + classes.map(CSS.escape).join('.');
     const siblings=node.parentElement ? [...node.parentElement.children].filter(x=>x.tagName===node.tagName) : [];
     if(siblings.length>1) part += `:nth-of-type(${siblings.indexOf(node)+1})`;
     parts.unshift(part);
     if(document.querySelectorAll(parts.join(' > ')).length===1) break;
     node=node.parentElement;
   }
   return parts.join(' > ');
 };
 document.addEventListener('mouseover', e=>{
   if(!window.__aiwpInspectEnabled) return;
   hovered?.removeAttribute('data-aiwp-hover');
   hovered=e.target;
   hovered.setAttribute('data-aiwp-hover','true');
 }, true);
 document.addEventListener('mouseout', e=>{ if(e.target===hovered){hovered.removeAttribute('data-aiwp-hover'); hovered=null;} }, true);
 document.addEventListener('click', e=>{
   if(!window.__aiwpInspectEnabled) return;
   e.preventDefault(); e.stopPropagation(); e.stopImmediatePropagation();
   const el=e.target;
   const cs=getComputedStyle(el);
   const computed=[
     `display: ${cs.display}`,
     `position: ${cs.position}`,
     `font-size: ${cs.fontSize}`,
     `font-weight: ${cs.fontWeight}`,
     `line-height: ${cs.lineHeight}`,
     `color: ${cs.color}`,
     `background-color: ${cs.backgroundColor}`,
     `padding: ${cs.padding}`,
     `margin: ${cs.margin}`,
     `border-radius: ${cs.borderRadius}`,
     `width: ${cs.width}`,
     `height: ${cs.height}`
   ].join('\n');
   chrome.webview.postMessage({
     type:'aiwp-element-selected',
     tag:el.tagName,
     id:el.id || '',
     classes:[...el.classList].join(' '),
     text:(el.innerText || el.getAttribute('aria-label') || '').trim().slice(0,1200),
     selector:selectorFor(el),
     computedStyle:computed
   });
 }, true);
 return true;
})()
""";
}
