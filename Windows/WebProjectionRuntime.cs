using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ChyguiSlide.Windows;

/// <summary>
/// Общая инициализация WebView2 для окна проектора и превью: один HTML, отдельные профили на инстанс.
/// </summary>
internal static class WebProjectionRuntime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<string, CoreWebView2Environment> Environments = new(StringComparer.OrdinalIgnoreCase);

    public static async Task PrepareWebViewAsync(WebView2 webView, string profileName = "projection")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var environment = await GetEnvironmentAsync(profileName).ConfigureAwait(true);
        await webView.EnsureCoreWebView2Async(environment);

        var core = webView.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 не создан.");

        ApplySettings(core);
        MapLocalMediaFolders(core);

        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "projection.html");
        if (!File.Exists(htmlPath))
        {
            throw new FileNotFoundException("Не найден Web/projection.html", htmlPath);
        }

        var htmlContent = await File.ReadAllTextAsync(htmlPath).ConfigureAwait(true);
        webView.NavigateToString(htmlContent);
    }

    private static async Task<CoreWebView2Environment> GetEnvironmentAsync(string profileName)
    {
        if (Environments.TryGetValue(profileName, out var existing))
        {
            return existing;
        }

        await Gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (Environments.TryGetValue(profileName, out existing))
            {
                return existing;
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChyguiSlide",
                "WebView2",
                // суффикс v2 — новый env с --autoplay-policy (кэш Environments в процессе + профиль)
                profileName + "-v2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: new CoreWebView2EnvironmentOptions
                {
                    // Иначе <video> с звуком не стартует с C#/автостарта (политика autoplay).
                    AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
                });
            Environments[profileName] = environment;
            return environment;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void ApplySettings(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
    }

    private static void MapLocalMediaFolders(CoreWebView2 core)
    {
        var backgroundsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide",
            "Backgrounds");
        Directory.CreateDirectory(backgroundsDir);
        core.SetVirtualHostNameToFolderMapping(
            "chygui.backgrounds",
            backgroundsDir,
            CoreWebView2HostResourceAccessKind.Allow);

        var mediaDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide",
            "Media");
        Directory.CreateDirectory(mediaDir);
        core.SetVirtualHostNameToFolderMapping(
            "chygui.media",
            mediaDir,
            CoreWebView2HostResourceAccessKind.Allow);
    }
}
