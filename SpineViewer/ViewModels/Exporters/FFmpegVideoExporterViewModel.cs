using Spine.Exporters;
using Spine;
using SpineViewer.Extensions;
using SpineViewer.Models;
using SpineViewer.Resources;
using SpineViewer.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Immutable;
using SpineViewer.ViewModels.MainWindow;

namespace SpineViewer.ViewModels.Exporters
{
    public class FFmpegVideoExporterViewModel(MainWindowViewModel vmMain) : VideoExporterViewModel(vmMain)
    {
        public static ImmutableArray<FFmpegVideoExporter.VideoFormat> VideoFormatOptions { get; } = Enum.GetValues<FFmpegVideoExporter.VideoFormat>().ToImmutableArray();
        public static ImmutableArray<FFmpegVideoExporter.ApngPredMethod> ApngPredMethodOptions { get; } = Enum.GetValues<FFmpegVideoExporter.ApngPredMethod>().ToImmutableArray();
        public static ImmutableArray<FFmpegVideoExporter.MovProfile> MovProfileOptions { get; } = Enum.GetValues<FFmpegVideoExporter.MovProfile>().ToImmutableArray();

        public FFmpegVideoExporter.VideoFormat Format 
        { 
            get => _format;
            set
            {
                if (!SetProperty(ref _format, value))
                    return;
                OnPropertyChanged(nameof(EnableParamLoop));
                OnPropertyChanged(nameof(EnableParamQuality));
                OnPropertyChanged(nameof(EnableParamLossless));
                OnPropertyChanged(nameof(EnableParamApngPred));
                OnPropertyChanged(nameof(EnableParamCrf));
                OnPropertyChanged(nameof(EnableParamProfile));
            }
        }
        protected FFmpegVideoExporter.VideoFormat _format = FFmpegVideoExporter.VideoFormat.Mp4;

        public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }
        protected bool _loop = true;

        public bool EnableParamLoop =>
            _format == FFmpegVideoExporter.VideoFormat.Gif ||
            _format == FFmpegVideoExporter.VideoFormat.Webp ||
            _format == FFmpegVideoExporter.VideoFormat.Apng;

        public int Quality { get => _quality; set => SetProperty(ref _quality, Math.Clamp(value, 0, 100)); }
        protected int _quality = 75;

        public bool EnableParamQuality =>
            _format == FFmpegVideoExporter.VideoFormat.Webp;

        public bool Lossless { get => _lossless; set => SetProperty(ref _lossless, value); }
        protected bool _lossless = false;

        public bool EnableParamLossless =>
            _format == FFmpegVideoExporter.VideoFormat.Webp;

        public FFmpegVideoExporter.ApngPredMethod PredMethod { get => _predMethod; set => SetProperty(ref _predMethod, value); }
        protected FFmpegVideoExporter.ApngPredMethod _predMethod = FFmpegVideoExporter.ApngPredMethod.Mixed;

        public bool EnableParamApngPred =>
            _format == FFmpegVideoExporter.VideoFormat.Apng;

        public int Crf { get => _crf; set => SetProperty(ref _crf, Math.Clamp(value, 0, 63)); }
        protected int _crf = 23;

        public bool EnableParamCrf =>
            _format == FFmpegVideoExporter.VideoFormat.Mp4 ||
            _format == FFmpegVideoExporter.VideoFormat.Webm ||
            _format == FFmpegVideoExporter.VideoFormat.Mkv;

        public FFmpegVideoExporter.MovProfile Profile { get => _profile; set => SetProperty(ref _profile, value); }
        protected FFmpegVideoExporter.MovProfile _profile = FFmpegVideoExporter.MovProfile.Yuv4444Extreme;

        public bool EnableParamProfile =>
            _format == FFmpegVideoExporter.VideoFormat.Mov;

        private string FormatSuffix => $".{_format.ToString().ToLowerInvariant()}";

        protected override void Export(SpineObjectModel[] models)
        {
            base.Export(models);
            if (!DialogService.ShowFFmpegVideoExporterDialog(this)) return;
            SpineObject[] spines = models.Select(m => m.GetSpineObject()).ToArray(); 

            if (_disableTrackLoop)
            {
                foreach (var sp in spines)
                    foreach (var tr in sp.AnimationState.IterTracks().Where(t => t is not null))
                        tr.Loop = false;
            }

            ProgressService.RunAsync((pr, ct) => ExportTask(spines, pr, ct), AppResource.Str_FFmpegVideoExporterTitle);
            foreach (var sp in spines) sp.Dispose();
        }

        /// <summary>
        /// VLote: export current model with the same FFmpeg engine and canvas
        /// configuration as the existing Export Video action. This method
        /// blocks until every file is actually encoded and checked.
        /// It throws on failure; callers MUST NOT move source folders then.
        /// Called from a Task.Run worker, not the WPF dispatcher.
        /// </summary>
        public void ExportVLote(SpineObjectModel model, string outputFolder,
                               VLoteExportOptions settings,
                               Action<string>? report, CancellationToken ct)
        {
            if (settings.Width < 2 || settings.Height < 2 ||
                settings.Fps < 1 || settings.Fps > 120)
                throw new ArgumentOutOfRangeException(nameof(settings));
            if (settings.Transparent && settings.Format == FFmpegVideoExporter.VideoFormat.Mp4)
                throw new ArgumentException("MP4/H.264 does not preserve alpha. Choose WebP/APNG/MOV or turn off transparency.");
            Directory.CreateDirectory(outputFolder);

            // SFML render textures must not be used from the preview and the
            // exporter at the same time; same workaround as ExportTask().
            _vmMain.SFMLRendererViewModel.StopRender();
            try
            {
                using var original = model.GetSpineObject();
                if (original.Data.Animations.Length == 0)
                    throw new InvalidOperationException("This model has no animations. Classify as NULL or check the files.");

                using var exporter = (FFmpegVideoExporter)GetExporter();
                exporter.Resolution = new(settings.Width, settings.Height);
                exporter.Fps = settings.Fps;
                exporter.Speed = settings.Speed;
                exporter.KeepLast = settings.KeepLast;
                exporter.Format = settings.Format;
                exporter.Crf = settings.Crf;
                exporter.Quality = settings.Quality;
                exporter.Lossless = settings.Lossless;
                exporter.Loop = settings.Loop;
                exporter.BackgroundColor = settings.Transparent
                    ? new SFML.Graphics.Color(0, 0, 0, 0)
                    : new SFML.Graphics.Color(0, 0, 0, 255);
                exporter.BackgroundImagePath = settings.Transparent
                    ? null
                    : _vmMain.SFMLRendererViewModel.BackgroundImagePath;

                if (!settings.AutoFit)
                {
                    using var view = _renderer.GetView();
                    var bounds = view.GetBounds().GetCanvasBounds(_renderer.Resolution, 0);
                    exporter.Size = bounds.Size;
                    exporter.Center = bounds.Position + bounds.Size / 2;
                    exporter.Rotation = view.Rotation;
                }

                var list = settings.AllAnimations
                    ? original.Data.Animations.ToArray()
                    : original.Data.Animations.Take(1).ToArray();

                foreach (var animation in list)
                {
                    ct.ThrowIfCancellationRequested();
                    report?.Invoke($"Exportando {model.Name} / {animation.Name}...");
                    using var current = original.Copy();
                    current.AnimationState.ClearTracks();
                    current.Skeleton.SetToSetupPose();
                    var track = current.AnimationState.SetAnimation(0, animation, true);
                    if (settings.DisableTrackLoop) track.Loop = false;
                    current.Update(0);

                    exporter.Duration = settings.Duration < 0
                        ? animation.Duration
                        : settings.Duration;
                    if (exporter.Duration <= 0)
                        throw new InvalidOperationException($"Animation has zero duration: {animation.Name}");
                    if (settings.AutoFit)
                        SetAutoResolutionAnimated(exporter, current);

                    var name = string.Join("_", new[] { model.Name, animation.Name }
                        .Select(x => string.Concat(x.Select(c =>
                            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))));
                    var extension = "." + settings.Format.ToString().ToLowerInvariant();
                    var destination = Path.Combine(outputFolder, name + extension);
                    // The same animation can occur twice in different exports.
                    // Never silently overwrite a previous successful export.
                    for (var index = 1; File.Exists(destination); index++)
                        destination = Path.Combine(outputFolder, $"{name} ({index}){extension}");

                    exporter.ProgressReporter = (total, done, message) =>
                        report?.Invoke($"{model.Name} / {animation.Name}  {done:0}/{total:0}");
                    try
                    {
                        exporter.Export(destination, ct, current);
                        if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
                            throw new IOException($"FFmpeg did not produce a valid non-empty output: {destination}");
                    }
                    catch
                    {
                        if (File.Exists(destination))
                            File.Delete(destination);
                        throw;
                    }
                }
            }
            finally
            {
                _vmMain.SFMLRendererViewModel.StartRender();
            }
        }

        protected override VideoExporter GetExporter()
        {
            return new FFmpegVideoExporter(_renderer.Resolution.X + _margin * 2, _renderer.Resolution.Y + _margin * 2)
            {
                BackgroundColor = new(_backgroundColor.R, _backgroundColor.G, _backgroundColor.B, _backgroundColor.A),
                BackgroundImagePath = _vmMain.SFMLRendererViewModel.BackgroundImagePath,
                BackgroundImageMode = _vmMain.SFMLRendererViewModel.BackgroundImageMode switch
                {
                    System.Windows.Media.Stretch.None => BaseExporter.BackgroundStretchMode.None,
                    System.Windows.Media.Stretch.Uniform => BaseExporter.BackgroundStretchMode.Uniform,
                    System.Windows.Media.Stretch.UniformToFill => BaseExporter.BackgroundStretchMode.UniformToFill,
                    System.Windows.Media.Stretch.Fill => BaseExporter.BackgroundStretchMode.Fill,
                    _ => BaseExporter.BackgroundStretchMode.Uniform,
                },

                Fps = _fps,
                Speed = _speed,
                KeepLast = _keepLast,
                Format = _format,
                Loop = _loop,
                Quality = _quality,
                Lossless = _lossless,
                PredMethod = _predMethod,
                Crf = _crf,
                Profile = _profile,
            };
        }

        protected override string GetOutputName(string? prefix)
        {
             if (string.IsNullOrWhiteSpace(prefix))
                 prefix = "video";

             return $"{prefix}{FormatSuffix}";
        }
    }
}
