using System.Text.Json;
using System.Windows;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using Microsoft.Web.WebView2.Core;

namespace AIWordPressManager.Desktop.Services;

public sealed class PuterWebViewBridge(IApplicationPathService paths) : IPuterAiBridge
{
    private CoreWebView2Environment? _environment;
    private PuterGatewayWindow? _window;

    public async Task<string> ChatAsync(string prompt, string model, CancellationToken cancellationToken = default)
    {
        var window = await GetWindowAsync(show: false, cancellationToken);
        if (!await window.IsSignedInAsync(cancellationToken))
            throw new AiProviderException(
                "Puter",
                401,
                "Puter is not connected. Open Settings, test Puter, and sign in with your Puter account first.",
                "Puter.js requires an authenticated user-pays session before AI requests can run.");

        return await window.ChatAsync(prompt, model, cancellationToken);
    }

    public async Task<AiProviderTestResult> ConnectAndTestAsync(string model, CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await GetWindowAsync(show: true, cancellationToken);
            window.Activate();
            var signedIn = await window.EnsureSignedInInteractivelyAsync(cancellationToken);
            if (!signedIn)
                return new(false, "Puter sign-in was not completed.", []);

            var models = await window.ListModelsAsync(cancellationToken);
            var testPrompt = "Return exactly this JSON array and nothing else: [{\"objectId\":\"test\",\"changeType\":\"SetTitle\",\"proposedValue\":\"Connection succeeded\",\"reason\":\"Puter test\",\"confidence\":0.9,\"riskLevel\":\"Low\"}]";
            var response = await window.ChatAsync(testPrompt, model, cancellationToken);
            var success = !string.IsNullOrWhiteSpace(response);
            return new(
                success,
                success
                    ? "Puter connection succeeded. No developer API key is required; usage is tied to the signed-in Puter user account."
                    : "Puter returned an empty response.",
                models);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message, []);
        }
    }

    private async Task<PuterGatewayWindow> GetWindowAsync(bool show, CancellationToken cancellationToken)
    {
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (_environment is null)
            {
                var folder = System.IO.Path.Combine(paths.GetApplicationDataDirectory(), "PuterWebView2");
                System.IO.Directory.CreateDirectory(folder);
                _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            }

            if (_window is null || !_window.IsLoaded)
            {
                _window = new PuterGatewayWindow(_environment, System.IO.Path.Combine(paths.GetApplicationDataDirectory(), "PuterGateway"))
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                _window.Closed += (_, _) => _window = null;
            }

            var openedForInitialization = false;
            if (!_window.IsVisible)
            {
                _window.Show();
                openedForInitialization = !show;
            }

            await _window.EnsureReadyAsync(cancellationToken);
            if (openedForInitialization) _window.Hide();
            return _window;
        }).Task.Unwrap();
    }
}
