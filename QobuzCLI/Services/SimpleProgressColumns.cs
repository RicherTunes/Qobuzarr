using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace QobuzCLI.Services;

/// <summary>
/// Simple transfer speed column for progress display
/// </summary>
public class SimpleTransferSpeedColumn : ProgressColumn
{
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        if (task.IsFinished)
            return new Text("-", Style.Plain.Foreground(Color.Grey));

        // Simple speed calculation - this will be improved with real tracking
        var speedText = "-- MB/s";
        return new Text(speedText, Style.Plain.Foreground(Color.Yellow));
    }

    public override int? GetColumnWidth(RenderOptions options) => 10;
}

/// <summary>
/// Simple downloaded column
/// </summary>
public class SimpleDownloadedColumn : ProgressColumn
{
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        if (task.MaxValue <= 0)
            return new Text("-", Style.Plain.Foreground(Color.Grey));

        var text = $"{task.Value:F0}%";
        var style = task.IsFinished
            ? Style.Plain.Foreground(Color.Green)
            : Style.Plain.Foreground(Color.Blue);

        return new Text(text, style);
    }

    public override int? GetColumnWidth(RenderOptions options) => 8;
}
