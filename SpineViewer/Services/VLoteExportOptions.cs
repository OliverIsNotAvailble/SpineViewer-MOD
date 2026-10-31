using Spine.Exporters;

namespace SpineViewer.Services;

public sealed class VLoteExportOptions
{
    public uint Width { get; init; } = 720;
    public uint Height { get; init; } = 720;
    public uint Fps { get; init; } = 30;
    public FFmpegVideoExporter.VideoFormat Format { get; init; } = FFmpegVideoExporter.VideoFormat.Mp4;
    public bool AllAnimations { get; init; } = true;
    public bool Transparent { get; init; }
    public bool AutoFit { get; init; }
    public bool KeepLast { get; init; } = true;
    public bool Loop { get; init; } = true;
    public bool Lossless { get; init; }
    public bool DisableTrackLoop { get; init; }
    public int Crf { get; init; } = 23;
    public int Quality { get; init; } = 95;
    public float Duration { get; init; } = -1f;
    public float Speed { get; init; } = 1f;
}
