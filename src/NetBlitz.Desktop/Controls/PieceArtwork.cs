using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using NetBlitz.Core;

namespace NetBlitz.Desktop.Controls;

/// <summary>Cburnett SVGs rendered as cached Avalonia geometry, sharp at any board size or display scale.</summary>
public static class PieceArtwork
{
    private const double ViewBoxSize = 45;
    private static readonly Dictionary<(PieceType, Side), DrawingGroup> Pieces = [];

    public static void Draw(DrawingContext context, Piece piece, Rect destination)
    {
        if (!Pieces.TryGetValue((piece.Type, piece.Side), out var drawing))
        {
            string name = (piece.Side == Side.White ? "w" : "b") + Chess.Letter(piece.Type);
            using var stream = AssetLoader.Open(new Uri($"avares://NetBlitz.Desktop/Assets/Pieces/Cburnett/{name}.svg"));
            // Only bundled, pinned assets are supported. Never resolve SVG DTDs or external resources.
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var document = XDocument.Load(reader);
            if ((string?)document.Root?.Attribute("viewBox") != "0 0 45 45")
                throw new InvalidDataException("Unexpected viewBox in Cburnett asset.");
            drawing = new DrawingGroup();
            ReadElement(document.Root!, new(), drawing);
            Pieces.Add((piece.Type, piece.Side), drawing);
        }
        using (context.PushTransform(Matrix.CreateScale(destination.Width / ViewBoxSize, destination.Height / ViewBoxSize)
            * Matrix.CreateTranslation(destination.X, destination.Y)))
            drawing.Draw(context);
    }

    // This is the small SVG subset present in the pinned Cburnett files, not a general SVG importer.
    private static void ReadElement(XElement element, Dictionary<string, string> inherited, DrawingGroup drawing)
    {
        var style = new Dictionary<string, string>(inherited);
        foreach (var attribute in element.Attributes()) style[attribute.Name.LocalName] = attribute.Value;
        foreach (string declaration in ((string?)element.Attribute("style") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = declaration.Split(':', 2);
            if (pair.Length == 2) style[pair[0].Trim()] = pair[1].Trim();
        }
        if (style.GetValueOrDefault("display") == "none") return;
        if (element.Attribute("transform") is not null) throw new InvalidDataException("Unexpected transform in Cburnett asset.");
        if (element.Name.LocalName is "svg" or "g")
        {
            foreach (var child in element.Elements()) ReadElement(child, style, drawing);
            return;
        }
        double Number(string name, double fallback = 0) => style.TryGetValue(name, out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
        Geometry geometry;
        switch (element.Name.LocalName)
        {
            case "path":
                geometry = StreamGeometry.Parse((style.GetValueOrDefault("fill-rule") == "evenodd" ? "F0 " : "F1 ") + style["d"]); break;
            case "circle":
            case "ellipse":
                double rx = Number("rx", Number("r")), ry = Number("ry", Number("r"));
                geometry = new EllipseGeometry(new Rect(Number("cx") - rx, Number("cy") - ry, rx * 2, ry * 2)); break;
            case "line":
                geometry = new LineGeometry(new Point(Number("x1"), Number("y1")), new Point(Number("x2"), Number("y2"))); break;
            default: throw new InvalidDataException($"Unexpected element in Cburnett asset: {element.Name.LocalName}");
        }
        string fill = style.GetValueOrDefault("fill", "#000000"), stroke = style.GetValueOrDefault("stroke", "none");
        var pen = stroke == "none" ? null : new Pen(Brush.Parse(stroke), Number("stroke-width", 1))
        {
            LineCap = style.GetValueOrDefault("stroke-linecap") switch { "round" => PenLineCap.Round, "square" => PenLineCap.Square, _ => PenLineCap.Flat },
            LineJoin = style.GetValueOrDefault("stroke-linejoin") switch { "round" => PenLineJoin.Round, "bevel" => PenLineJoin.Bevel, _ => PenLineJoin.Miter },
            MiterLimit = Number("stroke-miterlimit", 4)
        };
        drawing.Children.Add(new GeometryDrawing { Geometry = geometry, Brush = fill == "none" ? null : Brush.Parse(fill), Pen = pen });
    }
}

public sealed class PieceIcon : Control
{
    public Piece Piece { get; init; } = new(PieceType.Pawn, Side.White);
    public override void Render(DrawingContext context) => PieceArtwork.Draw(context, Piece, new Rect(Bounds.Size));
}
