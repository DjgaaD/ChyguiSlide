using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Data.Entities;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ChyguiSlide.ViewModels;

public sealed partial class ThemeWallpaperItem : ObservableObject
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".avi", ".webm"
    };

    public ThemeWallpaperItem(ThemeWallpaper entity)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        displayName = string.IsNullOrWhiteSpace(entity.DisplayName)
            ? Path.GetFileNameWithoutExtension(entity.FilePath)
            : entity.DisplayName;
        isVideo = IsVideoPath(entity.FilePath);
    }

    public ThemeWallpaper Entity { get; private set; }

    public Guid Id => Entity.Id;

    public string FilePath => Entity.FilePath;

    public string FileName => Path.GetFileName(Entity.FilePath);

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private BitmapImage? previewImage;

    [ObservableProperty]
    private bool isVideo;

    [ObservableProperty]
    private bool isFixedSelected;

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private bool isHovering;

    [ObservableProperty]
    private bool isEditingName;

    [ObservableProperty]
    private bool showRenameButton;

    [ObservableProperty]
    private bool showVideoBadge;

    partial void OnDisplayNameChanged(string value)
    {
        Entity.DisplayName = string.IsNullOrWhiteSpace(value)
            ? Path.GetFileNameWithoutExtension(Entity.FilePath)
            : value.Trim();
    }

    partial void OnIsHoveringChanged(bool value) => UpdateOverlayFlags();

    partial void OnIsEditingNameChanged(bool value) => UpdateOverlayFlags();

    partial void OnIsVideoChanged(bool value) => UpdateOverlayFlags();

    private void UpdateOverlayFlags()
    {
        ShowRenameButton = IsHovering && !IsEditingName;
        ShowVideoBadge = IsVideo && !IsEditingName;
    }

    public void BeginRename()
    {
        IsEditingName = true;
        IsHovering = true;
    }

    public void EndRename()
    {
        IsEditingName = false;
    }

    public void SyncFromEntity(ThemeWallpaper entity, bool isFixedSelected)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Id != Entity.Id)
        {
            return;
        }

        var pathChanged = !string.Equals(Entity.FilePath, entity.FilePath, StringComparison.OrdinalIgnoreCase);
        Entity = entity;

        if (!IsEditingName
            && !string.Equals(DisplayName, entity.DisplayName, StringComparison.CurrentCulture))
        {
            DisplayName = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? Path.GetFileNameWithoutExtension(entity.FilePath)
                : entity.DisplayName;
        }

        IsVideo = IsVideoPath(entity.FilePath);
        IsFixedSelected = isFixedSelected;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));

        if (pathChanged)
        {
            PreviewImage = null;
            HasPreview = false;
            _ = LoadPreviewAsync();
        }
    }

    public async Task LoadPreviewAsync()
    {
        var path = Entity.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            PreviewImage = null;
            HasPreview = false;
            return;
        }

        try
        {
            if (IsVideoPath(path))
            {
                IsVideo = true;
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    480,
                    ThumbnailOptions.ResizeThumbnail);
                if (thumb is null)
                {
                    PreviewImage = null;
                    HasPreview = false;
                    return;
                }

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(thumb);
                PreviewImage = bitmap;
                HasPreview = true;
            }
            else
            {
                IsVideo = false;
                var bitmap = new BitmapImage
                {
                    DecodePixelWidth = 480,
                    UriSource = new Uri(path, UriKind.Absolute)
                };
                PreviewImage = bitmap;
                HasPreview = true;
            }
        }
        catch
        {
            PreviewImage = null;
            HasPreview = false;
        }
    }

    public static bool IsVideoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return VideoExtensions.Contains(Path.GetExtension(path));
    }
}
