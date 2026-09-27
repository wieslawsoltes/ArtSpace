using ArtSpace.Core;

namespace ArtSpace.Documents;

/// <summary>Original editable artwork. Every element is a document object, not an embedded screenshot.</summary>
public static class IllustrationSample
{
    public static DesignDocument Create()
    {
        var document = new DesignDocument { Name = "Alpine Echoes", Pages = [new() { Name = "Artwork", Background = "#565656" }] };
        document.ColorStyles = new() { ["Midnight"] = "#203F49", ["Glacier"] = "#73A4A5", ["Ochre"] = "#E6AA67", ["Paper"] = "#F6F0E5", ["Pine"] = "#274B47" };
        var page = document.Pages[0];
        var poster = Artboard("01 — Alpine Echoes / Poster", 0, 0, 1040, 820, "#F6F0E5"); page.Nodes.Add(poster);
        Text(poster, "FIELD NOTES   /   VOLUME 07", 64, 32, 450, 20, 12, "#203F49", 600);
        Text(poster, "EST. 2026     •     ORIGINAL EDITION", 658, 32, 320, 20, 11, "#203F49");
        Text(poster, "ALPINE ECHOES", 57, 74, 940, 117, 92, "#203F49", 700);
        Text(poster, "A slower rhythm. A wider horizon.", 64, 196, 750, 34, 20, "#486668");
        var scene = poster.Add(new() { Kind = NodeKind.Frame, Name = "Landscape illustration", X = 64, Y = 250, Width = 912, Height = 468, ClipContent = true, Fills = [new() { Kind = FillKind.LinearGradient, Start = new(0, 0), End = new(0, 1), Stops = [new() { Offset = 0, Color = "#EDC68D" }, new() { Offset = 1, Color = "#F8E7C7" }] }] });
        Ellipse(scene, "Sun", 637, 37, 117, 117, "#EF985E");
        Ellipse(scene, "Sun halo", 609, 9, 173, 173, "#EF985E").Opacity = .15;
        Path(scene, "Distant ridge", "#A3B7A6", [new(0, 225), new(100, 114), new(175, 189), new(268, 82), new(355, 194), new(439, 123), new(557, 237), new(678, 111), new(792, 198), new(882, 142), new(912, 217), new(912, 390), new(0, 390)]);
        Path(scene, "Western summit", "#6E9190", [new(0, 281), new(84, 172), new(155, 220), new(290, 50), new(451, 304), new(507, 350), new(0, 370)]);
        Path(scene, "Western shadow", "#4E7479", [new(290, 50), new(451, 304), new(507, 350), new(221, 348), new(273, 218), new(246, 135)]);
        Path(scene, "Western snow", "#F1EDDC", [new(230, 126), new(290, 50), new(350, 145), new(301, 119), new(282, 150), new(267, 110), new(247, 140)]);
        Path(scene, "Eastern summit", "#537C80", [new(347, 351), new(529, 147), new(575, 205), new(618, 125), new(811, 337), new(912, 300), new(912, 380)]);
        Path(scene, "Eastern shadow", "#365F69", [new(618, 125), new(811, 337), new(912, 300), new(912, 380), new(562, 380), new(622, 271), new(578, 210)]);
        Path(scene, "Eastern snow", "#C2D3CB", [new(584, 189), new(618, 125), new(679, 193), new(640, 175), new(621, 196), new(610, 165)]);
        Rect(scene, "Glacial lake", 0, 348, 912, 120, "#83ADAB");
        Path(scene, "Reflection", "#608F94", [new(169, 354), new(488, 354), new(414, 381), new(464, 399), new(296, 418), new(351, 442), new(223, 425), new(264, 397), new(172, 380)]).Opacity = .8;
        for (var i = 0; i < 19; i++)
        {
            var x = 72 + ((i * 97) % 750); var y = 360 + (i * 29 % 103);
            Rect(scene, "Water highlight " + (i + 1), x, y, 35 + (i * 41 % 92), 1.5, "#C3D7C8").Opacity = .8;
        }
        Path(scene, "Near shore", "#345F5B", [new(0, 327), new(70, 309), new(150, 325), new(210, 351), new(335, 361), new(210, 374), new(99, 389), new(0, 405)]);
        for (var i = 0; i < 15; i++) Pine(scene, 10 + i * 17, 330 + Math.Sin(i * .7) * 13, 30 + (i * 19 % 72), i % 2 == 0 ? "#203F49" : "#2D514E");
        Path(scene, "Foreground island", "#23434B", [new(699, 468), new(766, 425), new(824, 414), new(888, 386), new(912, 392), new(912, 468)]);
        for (var i = 0; i < 9; i++) Pine(scene, 765 + i * 19, 443 - i * 4, 47 + (i * 37 % 82), "#203F49");
        Rect(poster, "Footer rule", 64, 748, 912, 1, "#B5C0B3");
        Text(poster, "46°35′ N  /  10°29′ E", 64, 768, 370, 20, 12, "#203F49", 600);
        Text(poster, "TAKE THE LONG WAY HOME", 671, 768, 305, 20, 12, "#203F49", 600);

        var badge = Artboard("02 — Trail badge", 1150, 0, 400, 400, "#E8B97E"); page.Nodes.Add(badge);
        Ellipse(badge, "Badge circle", 38, 38, 324, 324, "#203F49");
        Ellipse(badge, "Badge paper", 51, 51, 298, 298, "#F6F0E5");
        Ellipse(badge, "Badge sky", 65, 65, 270, 270, "#9AB8A6");
        Path(badge, "Badge summit", "#38666B", [new(88, 244), new(187, 114), new(218, 163), new(249, 127), new(322, 246)]);
        Path(badge, "Badge snow", "#F6F0E5", [new(161, 148), new(187, 114), new(218, 163), new(189, 144), new(178, 159)]);
        Rect(badge, "Badge band", 40, 240, 320, 67, "#203F49");
        Text(badge, "ALPINE CLUB", 67, 256, 278, 45, 32, "#F6F0E5", 700);

        var study = Artboard("03 — Color & curves", 1150, 490, 400, 330, "#F6F0E5"); page.Nodes.Add(study);
        Text(study, "PALETTE / FORM", 28, 25, 340, 30, 22, "#203F49", 700);
        var colors = document.ColorStyles.Values.ToArray();
        for (var i = 0; i < colors.Length; i++) Ellipse(study, "Palette " + i, 29 + i * 69, 77, 57, 57, colors[i]);
        var curve = new DesignNode { Kind = NodeKind.Path, Name = "Editable Bézier ribbon", X = 30, Y = 164, Width = 335, Height = 123, PathWidth = 335, PathHeight = 123, Fills = [], Strokes = [new() { Width = 13, Color = "#E6AA67", Cap = StrokeCap.Round, Join = StrokeJoin.Round }], Points = [new() { Position = new(0, 100), ControlOut = new(95, 100) }, new() { Position = new(138, 28), ControlIn = new(44, -40), ControlOut = new(225, 92) }, new() { Position = new(335, 0), ControlIn = new(227, 158) }] };
        study.Add(curve);
        document.RebuildParents(); return document;
    }
    private static DesignNode Artboard(string name, double x, double y, double width, double height, string color) => new() { Kind = NodeKind.Frame, Name = name, X = x, Y = y, Width = width, Height = height, Fill = color, ClipContent = true };
    private static DesignNode Rect(DesignNode parent, string name, double x, double y, double width, double height, string color) => parent.Add(new() { Name = name, X = x, Y = y, Width = width, Height = height, Fill = color });
    private static DesignNode Ellipse(DesignNode parent, string name, double x, double y, double width, double height, string color) => parent.Add(new() { Kind = NodeKind.Ellipse, Name = name, X = x, Y = y, Width = width, Height = height, Fill = color });
    private static void Text(DesignNode parent, string text, double x, double y, double width, double height, double size, string color, int weight = 400) => parent.Add(new() { Kind = NodeKind.Text, Name = text, Text = text, X = x, Y = y, Width = width, Height = height, FontSize = size, FontWeight = weight, Fill = color });
    private static DesignNode Path(DesignNode parent, string name, string color, Vec2[] points)
    {
        var left = points.Min(p => p.X); var top = points.Min(p => p.Y); var width = Math.Max(1, points.Max(p => p.X) - left); var height = Math.Max(1, points.Max(p => p.Y) - top);
        return parent.Add(new() { Kind = NodeKind.Path, Name = name, X = left, Y = top, Width = width, Height = height, PathWidth = width, PathHeight = height, Fill = color, Closed = true, Points = points.Select(p => new PathPoint { Position = new(p.X - left, p.Y - top) }).ToList() });
    }
    private static void Pine(DesignNode parent, double x, double ground, double height, string color)
    {
        var w = height * .28;
        Path(parent, "Pine tree", color, [new(x, ground - height), new(x - w * .55, ground - height * .54), new(x - w * .28, ground - height * .54), new(x - w, ground - height * .18), new(x - w * .12, ground - height * .18), new(x - w * .12, ground), new(x + w * .12, ground), new(x + w * .12, ground - height * .18), new(x + w, ground - height * .18), new(x + w * .28, ground - height * .54), new(x + w * .55, ground - height * .54)]);
    }
}
