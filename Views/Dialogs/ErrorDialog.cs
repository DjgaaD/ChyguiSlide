using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChyguiSlide.Views.Dialogs;

/// <summary>
/// Единый диалог ошибки с кнопкой «Скопировать ошибку».
/// </summary>
public static class ErrorDialog
{
    public static Task ShowAsync(string title, Exception ex) =>
        ShowAsync(null, title, FormatException(ex));

    public static Task ShowAsync(string title, string message) =>
        ShowAsync(null, title, message);

    public static Task ShowAsync(XamlRoot? xamlRoot, string title, Exception ex) =>
        ShowAsync(xamlRoot, title, FormatException(ex));

    public static async Task ShowAsync(XamlRoot? xamlRoot, string title, string message)
    {
        xamlRoot ??= TryGetXamlRoot();
        if (xamlRoot is null)
        {
            return;
        }

        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource();
            if (!dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        await ShowCoreAsync(xamlRoot, title, message).ConfigureAwait(true);
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
            {
                return;
            }

            await tcs.Task.ConfigureAwait(false);
            return;
        }

        await ShowCoreAsync(xamlRoot, title, message).ConfigureAwait(true);
    }

    private static async Task ShowCoreAsync(XamlRoot xamlRoot, string title, string message)
    {
        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        var copyButton = new Button
        {
            Content = "Скопировать ошибку",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };

        copyButton.Click += async (_, _) =>
        {
            var package = new DataPackage();
            package.SetText($"{title}\n\n{message}");
            Clipboard.SetContent(package);
            copyButton.Content = "Скопировано";
            await Task.Delay(1200);
            copyButton.Content = "Скопировать ошибку";
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children = { textBlock, copyButton }
                }
            },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }

    private static XamlRoot? TryGetXamlRoot()
    {
        try
        {
            return App.MainWindow?.Content?.XamlRoot;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var current = ex;
        var depth = 0;
        while (current is not null)
        {
            if (depth > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Inner ({current.GetType().Name}) ---");
            }
            else
            {
                sb.AppendLine($"{current.GetType().Name}:");
            }

            sb.AppendLine(current.Message);
            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }
}
