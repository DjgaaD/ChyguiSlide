using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace ChyguiSlide.Controls;

public sealed partial class OutlinedTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty FontFamilyNameProperty =
        DependencyProperty.Register(nameof(FontFamilyName), typeof(string), typeof(OutlinedTextBlock),
            new PropertyMetadata("Segoe UI", OnVisualChanged));

    public static readonly DependencyProperty DisplayFontSizeProperty =
        DependencyProperty.Register(nameof(DisplayFontSize), typeof(double), typeof(OutlinedTextBlock),
            new PropertyMetadata(16.0, OnVisualChanged));

    public static readonly DependencyProperty FontWeightValueProperty =
        DependencyProperty.Register(nameof(FontWeightValue), typeof(FontWeight), typeof(OutlinedTextBlock),
            new PropertyMetadata(default(FontWeight), OnVisualChanged));

    public static readonly DependencyProperty TextAlignmentValueProperty =
        DependencyProperty.Register(nameof(TextAlignmentValue), typeof(TextAlignment), typeof(OutlinedTextBlock),
            new PropertyMetadata(TextAlignment.Center, OnVisualChanged));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(OutlinedTextBlock),
            new PropertyMetadata(TextWrapping.NoWrap, OnVisualChanged));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty OutlineBrushProperty =
        DependencyProperty.Register(nameof(OutlineBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty OutlineThicknessProperty =
        DependencyProperty.Register(nameof(OutlineThickness), typeof(double), typeof(OutlinedTextBlock),
            new PropertyMetadata(0.0, OnVisualChanged));

    public static readonly DependencyProperty OutlineOpacityProperty =
        DependencyProperty.Register(nameof(OutlineOpacity), typeof(double), typeof(OutlinedTextBlock),
            new PropertyMetadata(1.0, OnVisualChanged));

    public OutlinedTextBlock()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FontFamilyName
    {
        get => (string)GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public double DisplayFontSize
    {
        get => (double)GetValue(DisplayFontSizeProperty);
        set => SetValue(DisplayFontSizeProperty, value);
    }

    public FontWeight FontWeightValue
    {
        get => (FontWeight)GetValue(FontWeightValueProperty);
        set => SetValue(FontWeightValueProperty, value);
    }

    public TextAlignment TextAlignmentValue
    {
        get => (TextAlignment)GetValue(TextAlignmentValueProperty);
        set => SetValue(TextAlignmentValueProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush? OutlineBrush
    {
        get => (Brush?)GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public double OutlineThickness
    {
        get => (double)GetValue(OutlineThicknessProperty);
        set => SetValue(OutlineThicknessProperty, value);
    }

    public double OutlineOpacity
    {
        get => (double)GetValue(OutlineOpacityProperty);
        set => SetValue(OutlineOpacityProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OutlinedTextBlock control)
        {
            control.Rebuild();
        }
    }

    private void Rebuild()
    {
        if (Root is null)
        {
            return;
        }

        Root.Children.Clear();
        var fill = FillBrush ?? Foreground;
        var thickness = OutlineThickness;
        var outline = OutlineBrush;

        if (thickness > 0.1 && outline is not null)
        {
            var opacity = Math.Clamp(OutlineOpacity, 0, 1);
            var outlineBrush = CloneBrushWithOpacity(outline, opacity);
            var radius = Math.Max(1, (int)Math.Ceiling(thickness));
            for (var r = 1; r <= radius; r++)
            {
                var scale = r * (thickness / radius);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4.0;
                    Root.Children.Add(CreateLine(outlineBrush, Math.Cos(angle) * scale, Math.Sin(angle) * scale));
                }
            }
        }

        Root.Children.Add(CreateLine(fill, 0, 0));
    }

    private TextBlock CreateLine(Brush? brush, double offsetX, double offsetY)
    {
        var weight = FontWeightValue.Weight == 0 ? FontWeights.Normal : FontWeightValue;
        var tb = new TextBlock
        {
            Text = Text ?? string.Empty,
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(FontFamilyName) ? "Segoe UI" : FontFamilyName),
            FontSize = DisplayFontSize,
            FontWeight = weight,
            TextAlignment = TextAlignmentValue,
            TextWrapping = TextWrapping,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false
        };

        if (Math.Abs(offsetX) > 0.01 || Math.Abs(offsetY) > 0.01)
        {
            tb.RenderTransform = new TranslateTransform { X = offsetX, Y = offsetY };
        }

        return tb;
    }

    private static Brush CloneBrushWithOpacity(Brush source, double opacity)
    {
        if (source is SolidColorBrush solid)
        {
            var c = solid.Color;
            return new SolidColorBrush(global::Windows.UI.Color.FromArgb(
                (byte)Math.Clamp((int)Math.Round(c.A * opacity), 0, 255),
                c.R, c.G, c.B));
        }

        return source;
    }
}
