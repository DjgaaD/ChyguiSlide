using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChyguiSlide.Controls;

/// <summary>
/// Содержимое блока куплета/стиха. Рамка, фон и hover — в общем стиле ListViewItem.
/// </summary>
public sealed partial class SelectableTextCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SelectableTextCard),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(
            nameof(Body),
            typeof(string),
            typeof(SelectableTextCard),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(
            nameof(IsHighlighted),
            typeof(bool),
            typeof(SelectableTextCard),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty ShowLiveBadgeProperty =
        DependencyProperty.Register(
            nameof(ShowLiveBadge),
            typeof(bool),
            typeof(SelectableTextCard),
            new PropertyMetadata(false, OnChanged));

    public SelectableTextCard()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Apply();
        Loaded += (_, _) => Apply();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public bool ShowLiveBadge
    {
        get => (bool)GetValue(ShowLiveBadgeProperty);
        set => SetValue(ShowLiveBadgeProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectableTextCard card)
        {
            card.Apply();
        }
    }

    private void Apply()
    {
        if (TitleText is null || BodyText is null || LiveBadge is null)
        {
            return;
        }

        TitleText.Text = Title ?? string.Empty;
        BodyText.Text = Body ?? string.Empty;
        BodyText.Visibility = string.IsNullOrWhiteSpace(Body)
            ? Visibility.Collapsed
            : Visibility.Visible;
        LiveBadge.Visibility = ShowLiveBadge && IsHighlighted
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (IsHighlighted)
        {
            TitleText.FontWeight = FontWeights.SemiBold;
            TitleText.Foreground = ResolveBrush("AccentTextFillColorPrimaryBrush")
                                   ?? ResolveBrush("AccentFillColorDefaultBrush");
        }
        else
        {
            TitleText.FontWeight = FontWeights.Normal;
            TitleText.Foreground = ResolveBrush("TextFillColorPrimaryBrush");
        }
    }

    private static Brush? ResolveBrush(string key) =>
        Application.Current.Resources[key] as Brush;
}
