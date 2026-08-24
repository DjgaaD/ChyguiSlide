using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace ChyguiSlide.Controls;

/// <summary>
/// Без обводки — TextBlock (автокегль/перенос).
/// С обводкой — Win2D stroke+fill на CanvasControl (один слой, плавный fade).
/// </summary>
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

    private bool _outlineMode;
    private bool _sizeHooked;

    public OutlinedTextBlock()
    {
        InitializeComponent();
        Clip = null;
        Loaded += (_, _) => RefreshPresentation();
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
            control.RefreshPresentation();
        }
    }

    private void RefreshPresentation()
    {
        if (FillText is null || OutlineCanvas is null)
        {
            return;
        }

        EnsureSizeHook();

        _outlineMode = OutlineThickness > 0.1
            && OutlineBrush is not null
            && !string.IsNullOrEmpty(Text);

        if (!_outlineMode)
        {
            OutlineCanvas.Visibility = Visibility.Collapsed;
            OutlineCanvas.Margin = new Thickness(0);
            FillText.Visibility = Visibility.Visible;
            FillText.Opacity = 1;
            ApplyTextBlockLayout();
            Height = double.NaN;
            MinHeight = 0;
            return;
        }

        ApplyTextBlockLayout();
        var overflow = GetOutlineOverflow();
        OutlineCanvas.Margin = new Thickness(-overflow);
        // Оставляем живой TextBlock в layout, но делаем невидимым:
        // его метрики полностью совпадают с режимом без контура.
        FillText.Visibility = Visibility.Visible;
        FillText.Opacity = 0;
        OutlineCanvas.Visibility = Visibility.Visible;
        OutlineCanvas.Invalidate();
    }

    private void EnsureSizeHook()
    {
        if (_sizeHooked)
        {
            return;
        }

        _sizeHooked = true;
        SizeChanged += (_, _) =>
        {
            if (_outlineMode)
            {
                OutlineCanvas?.Invalidate();
            }
        };
    }

    private void ApplyTextBlockLayout()
    {
        var weight = FontWeightValue.Weight == 0 ? FontWeights.Normal : FontWeightValue;
        FillText.Text = Text ?? string.Empty;
        FillText.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(FontFamilyName) ? "Segoe UI" : FontFamilyName);
        FillText.FontSize = DisplayFontSize;
        FillText.FontWeight = weight;
        FillText.TextAlignment = TextAlignmentValue;
        FillText.TextWrapping = TextWrapping;
        FillText.Foreground = FillBrush ?? Foreground;
        FillText.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private void OnOutlineCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_outlineMode)
        {
            return;
        }

        OutlineCanvas.Invalidate();
    }

    private void OnOutlineCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_outlineMode || string.IsNullOrEmpty(Text))
        {
            return;
        }

        try
        {
            var t = GetStrokeWidth();
            var pad = t + 2f;
            var layoutWidth = Math.Max(1f, (float)(FillText?.ActualWidth ?? sender.ActualWidth - pad * 2));
            if (layoutWidth < 1 || sender.ActualHeight < 1)
            {
                return;
            }

            using var format = CreateTextFormat();
            using var layout = new CanvasTextLayout(sender, Text, format, layoutWidth, float.MaxValue);
            using var geometry = CanvasGeometry.CreateText(layout);

            var inkTop = layout.DrawBounds.Top;
            var originY = pad + (float)Math.Max(0, -inkTop);
            var origin = new Vector2(pad, originY);

            var outlineColor = BrushToColor(OutlineBrush, OutlineOpacity);
            var fillColor = BrushToColor(FillBrush ?? Foreground, 1);

            args.DrawingSession.Antialiasing = CanvasAntialiasing.Antialiased;
            using var strokeStyle = new CanvasStrokeStyle
            {
                LineJoin = CanvasLineJoin.Round,
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            };

            // Как на Web-экране: кольцо смещений радиуса t + лёгкий stroke, затем заливка.
            const int steps = 16;
            for (var i = 0; i < steps; i++)
            {
                var angle = (float)(i / (double)steps * Math.PI * 2);
                var offset = new Vector2(MathF.Cos(angle) * t, MathF.Sin(angle) * t);
                args.DrawingSession.FillGeometry(geometry, origin + offset, outlineColor);
            }

            args.DrawingSession.DrawGeometry(geometry, origin, outlineColor, Math.Max(0.4f, t * 0.45f), strokeStyle);
            args.DrawingSession.FillGeometry(geometry, origin, fillColor);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OutlinedTextBlock] draw: {ex.Message}");
            FillText.Visibility = Visibility.Visible;
            FillText.Opacity = 1;
            OutlineCanvas.Visibility = Visibility.Collapsed;
        }
    }

    private float GetStrokeWidth()
    {
        // Та же величина, что у Web-проектора: thickness как есть, без урезания по кеглю.
        return (float)Math.Max(0.5, OutlineThickness);
    }

    private float GetOutlineOverflow() => GetStrokeWidth() + 2f;

    private CanvasTextFormat CreateTextFormat()
    {
        var weightValue = FontWeightValue.Weight == 0 ? FontWeights.Normal.Weight : FontWeightValue.Weight;
        return new CanvasTextFormat
        {
            FontSize = (float)Math.Max(1, DisplayFontSize),
            FontFamily = string.IsNullOrWhiteSpace(FontFamilyName) ? "Segoe UI" : FontFamilyName,
            FontWeight = new FontWeight { Weight = weightValue },
            WordWrapping = TextWrapping switch
            {
                TextWrapping.Wrap => CanvasWordWrapping.Wrap,
                TextWrapping.WrapWholeWords => CanvasWordWrapping.WholeWord,
                _ => CanvasWordWrapping.NoWrap
            },
            HorizontalAlignment = TextAlignmentValue switch
            {
                TextAlignment.Left => CanvasHorizontalAlignment.Left,
                TextAlignment.Right => CanvasHorizontalAlignment.Right,
                TextAlignment.Justify => CanvasHorizontalAlignment.Justified,
                _ => CanvasHorizontalAlignment.Center
            },
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
    }

    private static Color BrushToColor(Brush? brush, double opacity)
    {
        Color c = Color.FromArgb(255, 255, 255, 255);
        if (brush is SolidColorBrush solid)
        {
            c = solid.Color;
        }

        var a = (byte)Math.Clamp((int)Math.Round(c.A * Math.Clamp(opacity, 0, 1)), 0, 255);
        return Color.FromArgb(a, c.R, c.G, c.B);
    }
}
