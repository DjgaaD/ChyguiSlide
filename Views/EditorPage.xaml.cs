using ChyguiSlide.Services;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChyguiSlide.Views;

public sealed partial class EditorPage : Page
{
    public SongEditorViewModel ViewModel { get; }

    public EditorPage()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<SongEditorViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppUiThemeApplier.ApplyToElement(this);
    }

    /// <summary>
    /// Поле «Название» — однострочное. По умолчанию WinUI при вставке
    /// многострочного текста оставляет только первую строку. Вместо этого
    /// схлопываем переносы строк в пробел, чтобы название не терялось.
    /// </summary>
    private void OnTitleTextBoxPaste(object sender, TextControlPasteEventArgs e)
    {
        var textBox = sender as TextBox;
        var package = Clipboard.GetContent();
        if (textBox is null || !package.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var text = package.GetTextAsync().GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var singleLine = NormalizeSingleLine(text);
        if (string.Equals(text, singleLine, StringComparison.Ordinal))
        {
            return; // Одна строка — даём отработать обычному пути вставки
        }

        // Многострочный текст: вставляем сами, схлопнув переносы в пробелы.
        e.Handled = true;
        var start = Math.Min(textBox.SelectionStart, textBox.Text.Length);
        var length = Math.Min(textBox.SelectionLength, textBox.Text.Length - start);
        textBox.Text = textBox.Text.Remove(start, length).Insert(start, singleLine);
        textBox.SelectionStart = start + singleLine.Length;
        textBox.SelectionLength = 0;
    }

    private static string NormalizeSingleLine(string text) =>
        string.Join(' ',
            text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
}
