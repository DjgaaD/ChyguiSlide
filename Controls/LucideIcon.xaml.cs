using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace ChyguiSlide.Controls;

/// <summary>
/// Иконка Lucide (https://lucide.dev/icons). Файлы в Assets/Lucide/{kind}.svg.
/// Рисуется через Shape (stroke), цвет — из <see cref="Control.Foreground"/>.
/// </summary>
public sealed partial class LucideIcon : UserControl
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(string),
            typeof(LucideIcon),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(LucideIcon),
            new PropertyMetadata(20.0, OnSizeChanged));

    public static readonly DependencyProperty StrokeWidthProperty =
        DependencyProperty.Register(
            nameof(StrokeWidth),
            typeof(double),
            typeof(LucideIcon),
            new PropertyMetadata(2.0, OnVisualPropertyChanged));

    private static readonly Dictionary<string, string> SvgCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    private int _renderVersion;

    public LucideIcon()
    {
        InitializeComponent();
        IsTabStop = false;
        Width = 20;
        Height = 20;
        ActualThemeChanged += (_, _) => _ = RenderAsync();
        Loaded += (_, _) =>
        {
            ApplySize();
            _ = RenderAsync();
        };
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => ApplyStrokeBrush());
    }

    /// <summary>Имя файла Lucide без .svg, например plus, trash-2, book-open.</summary>
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LucideIcon icon)
        {
            _ = icon.RenderAsync();
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LucideIcon icon)
        {
            icon.ApplySize();
        }
    }

    private void ApplySize()
    {
        Width = Size;
        Height = Size;
    }

    private async Task RenderAsync()
    {
        var version = Interlocked.Increment(ref _renderVersion);
        var kind = (Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(kind) || IconCanvas is null)
        {
            return;
        }

        string? svg;
        try
        {
            svg = await LoadSvgAsync(kind).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (version != _renderVersion || svg is null || IconCanvas is null)
        {
            return;
        }

        try
        {
            BuildShapes(svg);
            ApplyStrokeBrush();
        }
        catch
        {
            IconCanvas.Children.Clear();
        }
    }

    private void BuildShapes(string svg)
    {
        IconCanvas.Children.Clear();

        var doc = XDocument.Parse(svg);
        var root = doc.Root;
        if (root is null)
        {
            return;
        }

        foreach (var el in root.Elements())
        {
            Shape? shape = el.Name.LocalName switch
            {
                "path" => CreatePath(el),
                "circle" => CreateCircle(el),
                "rect" => CreateRect(el),
                "line" => CreateLine(el),
                "polyline" => CreatePoly(el, closed: false),
                "polygon" => CreatePoly(el, closed: true),
                _ => null
            };

            if (shape is null)
            {
                continue;
            }

            shape.StrokeThickness = StrokeWidth;
            shape.StrokeStartLineCap = PenLineCap.Round;
            shape.StrokeEndLineCap = PenLineCap.Round;
            shape.StrokeLineJoin = PenLineJoin.Round;
            shape.Fill = null;
            shape.IsHitTestVisible = false;
            IconCanvas.Children.Add(shape);
        }
    }

    private void ApplyStrokeBrush()
    {
        if (IconCanvas is null)
        {
            return;
        }

        Brush brush;
        if (Foreground is Brush fg)
        {
            brush = fg;
        }
        else if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var resource)
                 && resource is Brush themeBrush)
        {
            brush = themeBrush;
        }
        else
        {
            brush = new SolidColorBrush(Microsoft.UI.Colors.Black);
        }

        foreach (var child in IconCanvas.Children.OfType<Shape>())
        {
            child.Stroke = brush;
            child.StrokeThickness = StrokeWidth;
        }
    }

    private static ShapePath? CreatePath(XElement el)
    {
        var d = (string?)el.Attribute("d");
        if (string.IsNullOrWhiteSpace(d))
        {
            return null;
        }

        return new ShapePath { Data = ParseGeometry(d) };
    }

    private static Ellipse? CreateCircle(XElement el)
    {
        var cx = ReadDouble(el, "cx");
        var cy = ReadDouble(el, "cy");
        var r = ReadDouble(el, "r");
        if (r <= 0)
        {
            return null;
        }

        var ellipse = new Ellipse
        {
            Width = r * 2,
            Height = r * 2
        };
        Canvas.SetLeft(ellipse, cx - r);
        Canvas.SetTop(ellipse, cy - r);
        return ellipse;
    }

    private static ShapePath? CreateRect(XElement el)
    {
        var x = ReadDouble(el, "x");
        var y = ReadDouble(el, "y");
        var w = ReadDouble(el, "width");
        var h = ReadDouble(el, "height");
        var rx = ReadDouble(el, "rx");
        var ry = ReadDouble(el, "ry", rx);
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);

        string data;
        if (rx <= 0 && ry <= 0)
        {
            data = FormattableString.Invariant($"M{x},{y} h{w} v{h} h{-w} Z");
        }
        else
        {
            data = FormattableString.Invariant(
                $"M{x + rx},{y} H{x + w - rx} A{rx},{ry} 0 0 1 {x + w},{y + ry} V{y + h - ry} A{rx},{ry} 0 0 1 {x + w - rx},{y + h} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + h - ry} V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z");
        }

        return new ShapePath { Data = ParseGeometry(data) };
    }

    private static Line? CreateLine(XElement el)
    {
        return new Line
        {
            X1 = ReadDouble(el, "x1"),
            Y1 = ReadDouble(el, "y1"),
            X2 = ReadDouble(el, "x2"),
            Y2 = ReadDouble(el, "y2")
        };
    }

    private static ShapePath? CreatePoly(XElement el, bool closed)
    {
        var points = (string?)el.Attribute("points");
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        var pairs = points
            .Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p, CultureInfo.InvariantCulture))
            .ToArray();

        if (pairs.Length < 4)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"M{pairs[0]},{pairs[1]}");
        for (var i = 2; i + 1 < pairs.Length; i += 2)
        {
            sb.Append(CultureInfo.InvariantCulture, $" L{pairs[i]},{pairs[i + 1]}");
        }

        if (closed)
        {
            sb.Append('Z');
        }

        return new ShapePath { Data = ParseGeometry(sb.ToString()) };
    }

    private static Geometry ParseGeometry(string data)
    {
        return (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
    }

    private static double ReadDouble(XElement el, string name, double fallback = 0)
    {
        var attr = el.Attribute(name);
        if (attr is null || string.IsNullOrWhiteSpace(attr.Value))
        {
            return fallback;
        }

        return double.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static async Task<string?> LoadSvgAsync(string kind)
    {
        await CacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (SvgCache.TryGetValue(kind, out var cached))
            {
                return cached;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        string? text = null;

        try
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri($"ms-appx:///Assets/Lucide/{kind}.svg"));
            text = await FileIO.ReadTextAsync(file);
        }
        catch
        {
            // unpackaged / layout without ms-appx mapping
        }

        if (text is null)
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Lucide", $"{kind}.svg");
            if (File.Exists(path))
            {
                text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            }
        }

        if (text is null)
        {
            return null;
        }

        await CacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            SvgCache[kind] = text;
        }
        finally
        {
            CacheLock.Release();
        }

        return text;
    }
}
