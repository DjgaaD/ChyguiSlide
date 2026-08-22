using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace ChyguiSlide.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;

    public LogsViewModel(ILoggingService loggingService)
    {
        _loggingService = loggingService;
        Logs = _loggingService.Logs;

        ClearCommand = new RelayCommand(ClearLogs);
        SaveCommand = new AsyncRelayCommand(SaveLogsAsync);
        CopyCommand = new RelayCommand(CopyLogs);
    }

    public ObservableCollection<string> Logs { get; }

    public IRelayCommand ClearCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CopyCommand { get; }

    private void ClearLogs()
    {
        _loggingService.Clear();
    }

    private void CopyLogs()
    {
        try
        {
            var content = new StringBuilder();
            foreach (var log in Logs)
            {
                content.AppendLine(log);
            }
            
            var dataPackage = new DataPackage();
            dataPackage.SetText(content.ToString());
            Clipboard.SetContent(dataPackage);
            
            StatusMessage = "Логи скопированы в буфер обмена";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка копирования: {ex.Message}";
        }
    }

    private async Task SaveLogsAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Text files", new List<string> { ".txt" });
            picker.SuggestedFileName = $"chyguilide-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await _loggingService.SaveToFileAsync(file.Path);
                StatusMessage = $"Логи сохранены: {file.Path}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    [ObservableProperty]
    private string? statusMessage;
}
