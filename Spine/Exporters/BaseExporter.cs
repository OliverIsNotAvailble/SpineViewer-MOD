using NLog;
using SFML.Graphics;
using SFML.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Spine.Exporters
{
    /// <summary>
    /// 导出类基类, 提供基本的帧渲染功能
    /// </summary>
    public abstract class BaseExporter : IDisposable
    {
        public enum BackgroundStretchMode
        {
            None,
            Uniform,
            UniformToFill,
            Fill
        }

        /// <summary>
        /// 进度回调函数
        /// </summary>
        /// <param name="total">任务总量</param>
        /// <param name="done">已完成量</param>
        /// <param name="promptText">需要设置的进度提示文本</param>
        public delegate void ProgressReporterHandler(float total, float done, string promptText);

        /// <summary>
        /// 日志器
        /// </summary>
        protected static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 用于渲染的画布
        /// </summary>
        protected RenderTexture? _renderTexture;

        /// <summary>
        /// 画布大小 (分辨率)
        /// </summary>
        protected Vector2u _resolution = new(100, 100);

        /// <summary>
        /// 用于 <see cref="_renderTexture"/> 的 <see cref="View"/> 对象
        /// </summary>
        protected View _renderView = new();

        /// <summary>
        /// 初始化导出器
        /// </summary>
        /// <param name="width">画布宽像素值</param>
        /// <param name="height">画布高像素值</param>
        public BaseExporter(uint width, uint height) : this(new(width, height)) { }

        /// <summary>
        /// 初始化导出器
        /// </summary>
        public BaseExporter(Vector2u resolution)
        {
            // XXX: 强制变成 2 的倍数, 防止像是 yuv420p 这种像素格式报错
            resolution.X = resolution.X >> 1 << 1;
            resolution.Y = resolution.Y >> 1 << 1;
            if (resolution.X <= 0 || resolution.Y <= 0)
                throw new ArgumentException($"Invalid resolution: {resolution}");
            _resolution = resolution;
        }

        /// <summary>
        /// 可选的进度回调函数
        /// </summary>
        public ProgressReporterHandler? ProgressReporter { get => _progressReporter; set => _progressReporter = value; }
        protected ProgressReporterHandler? _progressReporter;

        /// <summary>
/// 背景图片路径
/// </summary>
        public string? BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                 if (_backgroundImagePath == value)
                  return;

                 _backgroundImagePath = value;
                  DisposeBackgroundImage();
          }
        }
        protected string? _backgroundImagePath;


        public BackgroundStretchMode BackgroundImageMode { get => _backgroundImageMode; set => _backgroundImageMode = value; }
        protected BackgroundStretchMode _backgroundImageMode = BackgroundStretchMode.Uniform;

        protected Texture? _backgroundImageTexture;
        protected Sprite? _backgroundImageSprite;
      
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                var bcPma = value;
                var a = bcPma.A / 255f;
                bcPma.R = (byte)(bcPma.R * a);
                bcPma.G = (byte)(bcPma.G * a);
                bcPma.B = (byte)(bcPma.B * a);
                _backgroundColorPma = bcPma;
            }
        }
        protected Color _backgroundColor = Color.Black;

        /// <summary>
        /// 预乘后的背景颜色
        /// </summary>
        protected Color _backgroundColorPma = Color.Black;

        /// <summary>
        /// 画面分辨率
        /// <inheritdoc cref="RenderTexture.Size"/>
        /// </summary>
        public Vector2u Resolution
        {
            get => _resolution;
            set
            {
                // XXX: 强制变成 2 的倍数, 防止像是 yuv420p 这种像素格式报错
                value.X = value.X >> 1 << 1;
                value.Y = value.Y >> 1 << 1;
                if (value.X <= 0 || value.Y <= 0)
                {
                    _logger.Warn("Omit invalid exporter resolution: {0}", value);
                    return;
                }
                _resolution = value;
            }
        }

        /// <summary>
        /// <inheritdoc cref="View.Viewport"/>
        /// </summary>
        public FloatRect Viewport
        {
            get => _renderView.Viewport;
            set => _renderView.Viewport = value;
        }

        /// <summary>
        /// <inheritdoc cref="View.Center"/>
        /// </summary>
        public Vector2f Center
        {
            get => _renderView.Center;
            set => _renderView.Center = value;
        }

        /// <summary>
        /// <inheritdoc cref="View.Size"/>
        /// </summary>
        public Vector2f Size
        {
            get => _renderView.Size;
            set => _renderView.Size = value;
        }

        /// <summary>
        /// <inheritdoc cref="View.Rotation"/>
        /// </summary>
        public float Rotation
        {
            get => _renderView.Rotation;
            set => _renderView.Rotation = value;
        }

        /// <summary>
        /// 辅助函数, 用于获取 <see cref="_renderTexture"/> 对象
        /// </summary>
        protected RenderTexture GetRenderTexture()
        {
            // XXX: 调试的时候发现 RenderTexture 对象在子线程中进行渲染后, 主线程可能无法正常 Dispose 其资源
            // 所以改成调用时调用方自己临时申请和管理
            var tex = new RenderTexture(_resolution.X, _resolution.Y);
            tex.SetView(_renderView);
            return tex;
        }

        protected void DisposeBackgroundImage()
        {
            _backgroundImageSprite?.Dispose();
            _backgroundImageTexture?.Dispose();
            _backgroundImageSprite = null;
            _backgroundImageTexture = null;
        }
        protected void EnsureBackgroundImageLoaded()
            {
            if (_backgroundImageSprite is not null)
                return;
            if (string.IsNullOrWhiteSpace(_backgroundImagePath))
                return;
            if (!File.Exists(_backgroundImagePath))
            {
                _logger.Warn("Background image not found: {0}", _backgroundImagePath);
                return;
            }

            _backgroundImageTexture = new Texture(_backgroundImagePath);
            _backgroundImageSprite = new Sprite(_backgroundImageTexture)
            {
                Origin = new(_backgroundImageTexture.Size.X / 2f, _backgroundImageTexture.Size.Y / 2f)
            };
        }
        protected void DrawBackgroundImage()
        {
            if (_renderTexture is null)
                return;

             EnsureBackgroundImageLoaded();

            if (_backgroundImageSprite is null)
                 return;

            var bg = _backgroundImageSprite;
            using var view = new View(_renderView);

            var viewSize = view.Size;
            var bgSize = bg.Texture.Size;

            var scaleX = Math.Abs(viewSize.X / bgSize.X);
            var scaleY = Math.Abs(viewSize.Y / bgSize.Y);

            var signX = Math.Sign(viewSize.X);
            var signY = Math.Sign(viewSize.Y);

            if (_backgroundImageMode == BackgroundStretchMode.None)
            {
                scaleX = scaleY = 1f;
            }
            else if (_backgroundImageMode == BackgroundStretchMode.Uniform)
            {
                scaleX = scaleY = Math.Min(scaleX, scaleY);
            }
            else if (_backgroundImageMode == BackgroundStretchMode.UniformToFill)
            {
                scaleX = scaleY = Math.Max(scaleX, scaleY);
            }

            bg.Scale = new(signX * scaleX, signY * scaleY);
            bg.Position = view.Center;
            bg.Rotation = view.Rotation;

            _renderTexture.Draw(bg);
        }

        protected SFMLImageVideoFrame GetFrame(SpineObject[] spines)
        {
            if (_renderTexture is null)
                throw new InvalidOperationException("Caller must manage the RenderTexture object.");

            _renderTexture.SetActive(true);
            _renderTexture.Clear(_backgroundColorPma);

            DrawBackgroundImage();

            foreach (var sp in spines.Reverse()) _renderTexture.Draw(sp);

            _renderTexture.Display();
            var frame = new SFMLImageVideoFrame(_renderTexture.Texture.CopyToImage());
            _renderTexture.SetActive(false);
            return frame;
        }

        /// <summary>
        /// 导出给定的模型, 从前往后对应从上往下的渲染顺序
        /// </summary>
        /// <param name="output">输出路径, 一般而言都是文件路径, 少数情况指定的是文件夹</param>
        /// <param name="spines">要导出的模型, 从前往后对应从上往下的渲染顺序</param>
        public abstract void Export(string output, params SpineObject[] spines);

        #region IDisposable 接口实现

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                DisposeBackgroundImage();
                
                if (_renderTexture is not null)
                {
                    _logger.Warn("RenderTexture disposing");
                    _renderTexture?.Dispose();
                    _renderTexture = null;
                }
                _renderView?.Dispose();
                _renderView = null;
            }
            _disposed = true;
        }

        ~BaseExporter()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            if (_disposed)
            {
                GC.SuppressFinalize(this);
            }
        }

        #endregion
    }
}
