using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.ViewModels;

public sealed partial class AnnouncementEditorViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private Guid? _editingId;
    private int _sortOrder;

    public AnnouncementEditorViewModel(IServiceProvider services)
    {
        _services = services;
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
    }

    public event EventHandler<Announcement>? AnnouncementSaved;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private string? validationTitle;

    [ObservableProperty]
    private string? validationError;

    [ObservableProperty]
    private bool isValidationVisible;

    public IAsyncRelayCommand SaveCommand { get; }

    partial void OnContentChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    public void CreateNew()
    {
        _editingId = null;
        _sortOrder = 0;
        Title = string.Empty;
        Content = string.Empty;
        IsPinned = false;
        DismissValidation();
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void LoadFrom(Announcement announcement)
    {
        _editingId = announcement.Id;
        _sortOrder = announcement.SortOrder;
        Title = announcement.Title;
        Content = announcement.Content;
        IsPinned = announcement.IsPinned;
        DismissValidation();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Content) && !IsSaving;

    private async Task SaveAsync()
    {
        if (!CanSave())
        {
            ShowValidation("Сохранение", "Текст объявления не может быть пустым.");
            return;
        }

        try
        {
            IsSaving = true;
            using var scope = _services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAnnouncementService>();

            var entity = new Announcement
            {
                Id = _editingId ?? Guid.NewGuid(),
                SortOrder = _sortOrder,
                Title = Title,
                Content = Content,
                IsPinned = IsPinned,
                IsPermanent = true
            };

            var saved = await service.SaveAsync(entity);
            AnnouncementSaved?.Invoke(this, saved);
        }
        catch (Exception ex)
        {
            await ErrorDialog.ShowAsync("Ошибка сохранения объявления", ex);
        }
        finally
        {
            IsSaving = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void DismissValidation()
    {
        IsValidationVisible = false;
        ValidationTitle = null;
        ValidationError = null;
    }

    private void ShowValidation(string title, string message)
    {
        ValidationTitle = title;
        ValidationError = message;
        IsValidationVisible = true;
    }
}
