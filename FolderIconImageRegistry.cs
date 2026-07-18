using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.ProjectSystem;
using System.Xml.Linq;

namespace SolidFolderIcon;

internal static class FolderIconImageRegistry
{
    private const double CanvasSize = 24.0;
    private static readonly object SyncRoot = new();
    private static bool initialized;
    private static IImageHandle? closedHandle;
    private static IImageHandle? openHandle;

    public static bool TryGetIcons(out ProjectImageMoniker closedIcon, out ProjectImageMoniker openIcon)
    {
        lock (SyncRoot)
        {
            if (!initialized && !TryInitializeCore())
            {
                closedIcon = default;
                openIcon = default;
                return false;
            }

            closedIcon = closedHandle!.Moniker.ToProjectSystemType();
            openIcon = openHandle!.Moniker.ToProjectSystemType();
            return true;
        }
    }

    public static bool Initialize()
    {
        lock (SyncRoot)
        {
            return initialized || TryInitializeCore();
        }
    }

    private static bool TryInitializeCore()
    {
        if (initialized)
        {
            return true;
        }

        var imageLibrary = ImageLibrary.Default;
        if (imageLibrary is null)
        {
            return false;
        }

        closedHandle = imageLibrary.AddCustomImage(RenderSvgBitmap("Images/SolidFolderClosed.svg"), canTheme: false);
        openHandle = imageLibrary.AddCustomImage(RenderSvgBitmap("Images/SolidFolderOpen.svg"), canTheme: false);
        initialized = true;
        return true;
    }

    private static BitmapSource RenderSvgBitmap(string resourcePath)
    {
        using var stream = OpenResourceStream(resourcePath);
        using var reader = new StreamReader(stream);
        var document = XDocument.Load(new StringReader(reader.ReadToEnd()));

        var root = new DrawingGroup();

        foreach (var path in document.Root?.Elements(XName.Get("path", "http://www.w3.org/2000/svg")) ?? [])
        {
            var data = path.Attribute("d")?.Value
                       ?? throw new InvalidOperationException($"SVG path data was not found in '{resourcePath}'.");

            var fill = path.Attribute("fill")?.Value ?? "#78909C";
            var geometry = Geometry.Parse(data).Clone();
            var brush = (Brush)new BrushConverter().ConvertFromString(fill)!;
            brush.Freeze();
            root.Children.Add(new GeometryDrawing(brush, null, geometry));
        }

        if (root.Children.Count == 0)
        {
            throw new InvalidOperationException($"SVG path element was not found in '{resourcePath}'.");
        }

        root.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawDrawing(root);
        }

        var bitmap = new RenderTargetBitmap((int)CanvasSize, (int)CanvasSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Stream OpenResourceStream(string resourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name!;
        var uri = new Uri($"pack://application:,,,/{assemblyName};component/{resourcePath}", UriKind.Absolute);
        var info = Application.GetResourceStream(uri);

        if (info?.Stream is null)
        {
            throw new FileNotFoundException($"Embedded resource '{resourcePath}' was not found.");
        }

        return info.Stream;
    }
}
