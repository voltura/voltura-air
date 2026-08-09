using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenDraftLayoutValidator(
    int port,
    CustomScreenService service)
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(12);

    public async Task<IReadOnlyList<CustomScreenLayoutIssue>> ValidateAsync(
        CustomScreenDefinition draft,
        CancellationToken cancellationToken)
    {
        using var lease = service.BeginDraftPreview(draft);
        using var browser = new WebView2();
        var window = CreateRenderWindow(browser, 360, 640);
        try
        {
            window.Show();
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltura Air",
                "WebView2 Preview");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await browser.EnsureCoreWebView2Async(environment);
            Configure(browser.CoreWebView2);

            var navigated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigationCompleted(
                object? _,
                CoreWebView2NavigationCompletedEventArgs eventArgs) =>
                navigated.TrySetResult(eventArgs.IsSuccess);
            browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            try
            {
                browser.Source = new UriBuilder(Uri.UriSchemeHttp, "127.0.0.1", port)
                {
                    Query = $"customScreenPreview={Uri.EscapeDataString(lease.ScreenId)}"
                }.Uri;
                if (!await navigated.Task.WaitAsync(
                        ValidationTimeout,
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "The mobile renderer could not load the validation preview.");
                }
            }
            finally
            {
                browser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            }

            await WaitForRendererAsync(browser.CoreWebView2, cancellationToken);
            var issues = new List<CustomScreenLayoutIssue>();
            issues.AddRange(await MeasureAsync(
                window,
                browser,
                360,
                640,
                "portrait",
                cancellationToken));
            issues.AddRange(await MeasureAsync(
                window,
                browser,
                640,
                360,
                "landscape",
                cancellationToken));
            return issues;
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateRenderWindow(
        WebView2 browser,
        int width,
        int height)
    {
        browser.Width = width;
        browser.Height = height;
        return new Window
        {
            Width = width,
            Height = height,
            Content = browser,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = 0.01,
            Left = SystemParameters.VirtualScreenLeft - width - 100,
            Top = SystemParameters.VirtualScreenTop - height - 100
        };
    }

    private static void Configure(CoreWebView2 browser)
    {
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.IsStatusBarEnabled = false;
        browser.Settings.IsZoomControlEnabled = false;
    }

    private static async Task WaitForRendererAsync(
        CoreWebView2 browser,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ValidationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await browser.ExecuteScriptAsync(
                    "document.querySelector('.custom-screen-preview-notice') !== null") ==
                "true")
            {
                return;
            }
            await Task.Delay(50, cancellationToken);
        }
        throw new TimeoutException("The mobile renderer did not become ready.");
    }

    private static async Task<IReadOnlyList<CustomScreenLayoutIssue>> MeasureAsync(
        Window window,
        WebView2 browser,
        int width,
        int height,
        string orientation,
        CancellationToken cancellationToken)
    {
        window.Width = width;
        window.Height = height;
        browser.Width = width;
        browser.Height = height;
        await window.Dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.Render,
            cancellationToken);
        await Task.Delay(100, cancellationToken);
        var script = $$"""
            (() => {
              const orientation = {{JsonSerializer.Serialize(orientation)}};
              const issues = [];
              const buttons = [...document.querySelectorAll('.custom-screen-button')];
              for (const button of buttons) {
                const id = button.dataset.customScreenButtonId ?? '';
                const label = button.getAttribute('aria-label') ?? 'Button';
                const size = [...button.classList].find(value => value.startsWith('size-'))?.slice(5) ?? 'standard';
                const rect = button.getBoundingClientRect();
                const section = button.closest('.custom-screen-section')?.getBoundingClientRect();
                if (rect.left < -1 || rect.right > innerWidth + 1 ||
                    (section && (rect.left < section.left - 1 || rect.right > section.right + 1 || rect.bottom > section.bottom + 1))) {
                  issues.push({ kind: 'button', buttonId: id, label, orientation, size });
                }
                for (const span of button.querySelectorAll(':scope > span:not(.custom-screen-pending)')) {
                  if (span.scrollWidth > span.clientWidth + 1 || span.scrollHeight > span.clientHeight + 1) {
                    issues.push({ kind: 'label', buttonId: id, label: span.textContent ?? label, orientation, size });
                  }
                }
              }
              if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
                issues.push({ kind: 'page', buttonId: '', label: 'Screen', orientation, size: '' });
              }
              return issues;
            })()
            """;
        var encoded = await browser.CoreWebView2.ExecuteScriptAsync(script)
            .WaitAsync(ValidationTimeout, cancellationToken);
        return DecodeLayoutIssues(encoded);
    }

    internal static IReadOnlyList<CustomScreenLayoutIssue> DecodeLayoutIssues(
        string encoded)
    {
        using var document = JsonDocument.Parse(encoded);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The mobile renderer returned an unexpected validation result.");
        }

        return document.RootElement.Deserialize<CustomScreenLayoutIssue[]>(
            CustomScreenJson.Exact) ?? [];
    }
}
