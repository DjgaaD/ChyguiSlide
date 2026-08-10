using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views.Dialogs;

public static class AppUpdateDialog
{
    public static async Task CheckOnStartupAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            return;
        }

        try
        {
            var updates = App.AppHost.Services.GetRequiredService<IAppUpdateService>();
            var info = await updates.CheckForUpdateAsync();
            if (info is null)
            {
                return;
            }

            await PromptAndInstallAsync(xamlRoot, updates, info, showNoUpdate: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Startup check failed: {ex.Message}");
        }
    }

    public static async Task CheckManualAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            await ErrorDialog.ShowAsync("Обновление", "Окно не готово. Откройте настройки ещё раз.");
            return;
        }

        try
        {
            var updates = App.AppHost.Services.GetRequiredService<IAppUpdateService>();
            var info = await updates.CheckForUpdateAsync();
            if (info is null)
            {
                var ok = new ContentDialog
                {
                    Title = "Обновления",
                    Content = $"У вас актуальная версия: {AppVersionInfo.DisplayVersion}",
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };
                await ok.ShowAsync();
                return;
            }

            await PromptAndInstallAsync(xamlRoot, updates, info, showNoUpdate: true);
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync(xamlRoot, "Не удалось проверить обновления", ex);
        }
    }

    private static async Task PromptAndInstallAsync(
        XamlRoot xamlRoot,
        IAppUpdateService updates,
        AppUpdateInfo info,
        bool showNoUpdate)
    {
        _ = showNoUpdate;

        var projection = App.AppHost.Services.GetService<IProjectionDisplayService>();
        var projectionNote = projection?.IsOpen == true
            ? "\n\nСейчас открыта трансляция — перед установкой она будет закрыта."
            : string.Empty;

        var scroll = new ScrollViewer
        {
            MaxHeight = 360,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Text =
                    $"Доступна версия {info.DisplayVersion} (сейчас {AppVersionInfo.DisplayVersion}).\n\n" +
                    $"{info.Changelog}{projectionNote}"
            }
        };

        var prompt = new ContentDialog
        {
            Title = "Доступно обновление",
            Content = scroll,
            PrimaryButtonText = "Обновить",
            CloseButtonText = "Позже",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        if (await prompt.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (projection?.IsOpen == true)
        {
            try { projection.Hide(); } catch { /* ignore */ }
        }

        var progressText = new TextBlock { Text = "Скачивание обновления…" };
        var bar = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 1 };
        var panel = new StackPanel { Spacing = 12, Children = { progressText, bar } };
        var progressDialog = new ContentDialog
        {
            Title = "Обновление ChyguiSlide",
            Content = panel,
            XamlRoot = xamlRoot,
            CloseButtonText = "Отмена"
        };

        var cts = new CancellationTokenSource();
        progressDialog.CloseButtonClick += (_, _) =>
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        };

        var progress = new Progress<double>(p =>
        {
            bar.IsIndeterminate = false;
            bar.Value = p;
            progressText.Text = $"Скачивание… {(int)(p * 100)}%";
        });

        var showTask = progressDialog.ShowAsync().AsTask();
        string installerPath;
        try
        {
            installerPath = await updates.DownloadInstallerAsync(info, progress, cts.Token);
            progressText.Text = "Запуск установщика…";
            updates.StartInstaller(installerPath);
        }
        catch (OperationCanceledException)
        {
            progressDialog.Hide();
            try { await showTask.ConfigureAwait(true); } catch { /* ignore */ }
            return;
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            try { await showTask.ConfigureAwait(true); } catch { /* ignore */ }
            await ErrorDialog.ShowAsync(xamlRoot, "Не удалось установить обновление", ex);
            return;
        }

        progressDialog.Hide();
        try { await showTask.ConfigureAwait(true); } catch { /* ignore */ }

        // Установщик сам закроет/обновит приложение; выходим из текущего процесса.
        Application.Current.Exit();
    }
}
