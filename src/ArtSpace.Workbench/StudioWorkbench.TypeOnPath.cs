using ArtSpace.Illustration;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private async Task CreateTypeOnPathAsync()
    {
        Surface.FinishTextEdit(true); Surface.FinishPath(false);
        if (Session.SelectionRoots.Count == 2)
        {
            TypeOnPathOperations.Attach(Session, Surface.Renderer); Surface.FocusCanvas(); return;
        }
        if (Session.SelectionRoots.Count != 1 || !TypeOnPathOperations.CanUseBaseline(Session.SelectionRoots[0]))
            throw new InvalidOperationException("Select one vector contour, or select a text object and a sibling contour.");
        var text = await PromptAsync("Type on a Path", "Type on a Path");
        if (text is null) return;
        TypeOnPathOperations.Create(Session, Surface.Renderer, text); Surface.FocusCanvas();
    }

    private void BuildTypeOnPath()
    {
        Inspect("Type on a Path", null, (body, b) =>
        {
            void ChangeOptions(string label, Action<TypeOnPathOptions> edit) => Run(() => TypeOnPathOperations.Update(Session, label, edit));
            body.Children.Add(Studio.Columns(
                (b.Number("Start %", () => InspectedNode.TextPath!.Start * 100,
                    value => ChangeOptions("Start path text", o => o.Start = Math.Min(o.End, value / 100)), 0, 100), -1),
                (b.Number("End %", () => InspectedNode.TextPath!.End * 100,
                    value => ChangeOptions("End path text", o => o.End = Math.Max(o.Start, value / 100)), 0, 100), -1)));
            body.Children.Add(b.Choice(Enum.GetNames<PathTextAlignment>(), () => InspectedNode.TextPath!.Alignment.ToString(),
                value => ChangeOptions("Align type to path", o => o.Alignment = Enum.Parse<PathTextAlignment>(value)), "Align type to path"));
            body.Children.Add(b.Number("Baseline shift", () => InspectedNode.TextPath!.BaselineShift,
                value => ChangeOptions("Shift path baseline", o => o.BaselineShift = value), -10000, 10000));
            body.Children.Add(b.Check("Flip along path", () => InspectedNode.TextPath!.Flip,
                value => ChangeOptions("Flip path text", o => o.Flip = value)));
            body.Children.Add(b.Text(() =>
            {
                var status = Surface.Renderer.GetTypeOnPathStatus(InspectedNode);
                return (status.Overflow ? "Overflow · " : "Fits · ") + status.VisibleGlyphs + " / " + status.TotalGlyphs
                    + " characters · " + Numbers.Format(status.PathLength) + " px path";
            }, 10, Studio.Muted));
            body.Children.Add(b.Button(() => "Edit baseline anchors", () => Run(Surface.EnterPathEditing)));
            body.Children.Add(b.Button(() => "Move text brackets", () => { Session.Tool = EditorTool.Move; Surface.FocusCanvas(); }));
            body.Children.Add(b.Button(() => "Create Outlines", () => Run(() => PathOperations.CreateOutlines(Session, Surface.Renderer))));
            body.Children.Add(b.Button(() => "Convert to area text", () => Run(() => TypeOnPathOperations.ConvertToAreaText(Session))));
        });
    }
}
