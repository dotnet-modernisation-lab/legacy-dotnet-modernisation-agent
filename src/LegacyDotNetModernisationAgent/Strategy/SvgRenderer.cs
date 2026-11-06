using SkiaSharp;
using Svg.Skia;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>Turns the overview SVG into a PNG so it can sit inside a Word document. The PNG bytes depend on the fonts installed; the document text does not.</summary>
public static class SvgRenderer
{
    public static (byte[] Png, int Width, int Height) ToPng(string svg, float scale = 2f)
    {
        using var drawing = new SKSvg();

        var picture = drawing.FromSvg(svg) ?? throw new InvalidOperationException("The diagram could not be rendered.");
        var bounds = picture.CullRect;
        var width = (int)Math.Ceiling(bounds.Width * scale);
        var height = (int)Math.Ceiling(bounds.Height * scale);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return (data.ToArray(), width, height);
    }
}
