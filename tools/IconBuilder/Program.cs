using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Generate the checked-in, multi-resolution Windows icon without external dependencies.
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
if (args.Length == 2)
{
    using var icon = Icon.ExtractAssociatedIcon(Path.GetFullPath(args[1])) ?? throw new Exception("No executable icon");
    using var bitmap = icon.ToBitmap();
    bitmap.Save(Path.Combine(output, "exe-icon.png"), ImageFormat.Png);
    Console.WriteLine($"Extracted embedded executable icon: {icon.Width} × {icon.Height}");
    return;
}
int[] sizes = [16, 32, 48, 64, 128, 256];
var images = new List<byte[]>();
foreach (var size in sizes)
{
    using var high = new Bitmap(size * 4, size * 4);
    using (var graphics = Graphics.FromImage(high))
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 8f, size / 8f);
        using var tile = new GraphicsPath();
        tile.AddArc(.5f, .5f, 13, 13, 180, 90);
        tile.AddArc(18.5f, .5f, 13, 13, 270, 90);
        tile.AddArc(18.5f, 18.5f, 13, 13, 0, 90);
        tile.AddArc(.5f, 18.5f, 13, 13, 90, 90);
        tile.CloseFigure();
        using var background = new SolidBrush(Color.FromArgb(19, 23, 31));
        graphics.FillPath(background, tile);
        using var border = new Pen(Color.FromArgb(60, 70, 87), 1);
        graphics.DrawPath(border, tile);
        using var blue = new SolidBrush(Color.FromArgb(114, 190, 255));
        using var orange = new SolidBrush(Color.FromArgb(255, 172, 114));
        graphics.FillRectangle(blue, 6, 17, 5, 9);
        graphics.FillRectangle(blue, 13, 11, 5, 15);
        graphics.FillRectangle(orange, 20, 5, 5, 21);
    }
    using var bitmap = new Bitmap(size, size);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(high, new Rectangle(0, 0, size, size));
    }
    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    images.Add(png.ToArray());
    bitmap.Save(Path.Combine(output, $"icon-{size}.png"), ImageFormat.Png);
}
using (var stream = File.Create(Path.Combine(output, "CodexBar.ico")))
using (var writer = new BinaryWriter(stream))
{
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    var offset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(images[i].Length); writer.Write(offset);
        offset += images[i].Length;
    }
    foreach (var png in images) writer.Write(png);
}
File.WriteAllText(Path.Combine(output, "CodexBar.svg"), """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <rect x=".5" y=".5" width="31" height="31" rx="6.5" fill="#13171f" stroke="#3c4657"/>
  <path fill="#72beff" d="M6 17h5v9H6zM13 11h5v15h-5z"/>
  <path fill="#ffac72" d="M20 5h5v21h-5z"/>
</svg>
""");
Console.WriteLine("Created CodexBar.ico: 16, 32, 48, 64, 128, 256 px (32-bit PNG frames)");
