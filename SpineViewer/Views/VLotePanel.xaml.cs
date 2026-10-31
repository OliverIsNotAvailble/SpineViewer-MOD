using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Spine.Exporters;
using SpineViewer.Models;
using SpineViewer.Services;
using SpineViewer.ViewModels.MainWindow;

namespace SpineViewer.Views;

/// <summary>
/// Interactive batch queue embedded in MainWindow's third left-hand tab.
/// Keeps SFML preview and canvas controls in the existing main viewport.
/// </summary>
public partial class VLotePanel : UserControl
{
    private MainWindowViewModel? _vm;
    private VLoteQueue? _queue;
    private SpineObjectModel? _active;
    private CancellationTokenSource? _cancel;
    private bool _busy;

    public event Action<string>? ProgressChanged;

    public VLotePanel()
    {
        InitializeComponent();
        Format.ItemsSource = Enum.GetValues<FFmpegVideoExporter.VideoFormat>();
        Format.SelectedItem = FFmpegVideoExporter.VideoFormat.Mp4;
    }

    public void SetContext(MainWindowViewModel vm) => _vm = vm;

    public string ProgressText => _queue is null
        ? "VLote 0.1 — selecione a pasta raiz"
        : $"VLote — {_queue.Progress}  |  " +
          (_queue.Current is null ? "Fila concluída" : _queue.Current.Name);

    private void Publish(string? detail = null)
    {
        if (detail is not null) Detail.Text = detail;
        Counter.Text = _queue?.Progress ?? "0/0";
        ProgressChanged?.Invoke(ProgressText + (detail is null ? "" : $"  |  {detail}"));
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (DialogService.ShowOpenFolderDialog(out var path) && path is not null)
        {
            RootPath.Text = path;
            Scan();
        }
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => Scan();

    private void Scan()
    {
        if (_busy || _vm is null) return;
        try
        {
            if (_vm.SpineObjects.Count > 0)
            {
                var choice = MessageBox.Show(
                    "VLote vai descarregar os modelos já abertos para mostrar apenas o próximo arquivo. Continuar?",
                    "VLote 0.1", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes) return;
            }
            UnloadModels(true);
            _queue = new VLoteQueue(RootPath.Text.Trim().Trim('"'));
            if (_queue.Total == 0)
                Publish("Nenhum .skel/.skel.bytes ou .json com atlas encontrado nas subpastas. " +
                        "Os arquivos diretamente na raiz não são movidos; coloque-os em uma subpasta.");
            else
                LoadNext();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "VLote: erro ao procurar arquivos",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Publish("Falha na leitura da pasta.");
        }
    }

    private void UnloadModels(bool all)
    {
        if (_vm is null) return;
        var models = all
            ? _vm.SpineObjects.ToArray()
            : _active is null ? [] : new[] { _active };
        if (models.Length == 0) return;

        _vm.SFMLRendererViewModel.StopRender();
        try
        {
            foreach (var model in models)
            {
                lock (_vm.SpineObjects.Lock)
                    _vm.SpineObjects.Remove(model);
                model.Dispose();
            }
            _vm.SpineObjectTabViewModel.SelectedObjects = [];
        }
        finally
        {
            _vm.SFMLRendererViewModel.StartRender();
            _active = null;
        }
    }

    private void LoadNext()
    {
        if (_vm is null || _queue is null) return;
        UnloadModels(false);
        CurrentName.Text = _queue.Current is null
            ? "Fila concluída ✔" : _queue.Current.Name;
        CurrentPath.Text = _queue.Current?.SkelPath ?? "";
        ExportButton.IsEnabled = NullButton.IsEnabled = _queue.Current is not null;
        if (_queue.Current is null)
        {
            Publish("Terminado. As pastas classificadas foram movidas para _OK e _Null.");
            return;
        }

        try
        {
            _vm.SpineObjectListViewModel.AddSpineObjectFromFileList(
                new[] { _queue.Current.SkelPath });
            lock (_vm.SpineObjects.Lock)
                _active = _vm.SpineObjects.FirstOrDefault(m =>
                    string.Equals(m.SkelPath, _queue.Current.SkelPath,
                                  StringComparison.OrdinalIgnoreCase));

            Publish(_active is null
                ? "Não foi possível abrir o modelo. Verifique o atlas e selecione NULL para descartá-lo."
                : "Preview carregado. Ajuste canvas/posição e use EXPORTAR ou NULL.");
            ExportButton.IsEnabled = _active is not null;
        }
        catch (Exception ex)
        {
            _active = null;
            ExportButton.IsEnabled = false;
            Publish($"Falha ao carregar: {ex.Message}. Use NULL para avançar.");
        }
    }

    private VLoteExportOptions Options()
    {
        static uint ParseUInt(TextBox text, uint min, uint max, string field)
        {
            if (!uint.TryParse(text.Text.Trim(), out var value) ||
                value < min || value > max)
                throw new ArgumentException($"{field}: use um valor de {min} a {max}.");
            return value;
        }
        static int ParseInt(TextBox text, int min, int max, string field)
        {
            if (!int.TryParse(text.Text.Trim(), out var value) ||
                value < min || value > max)
                throw new ArgumentException($"{field}: use um valor de {min} a {max}.");
            return value;
        }
        if (!float.TryParse(SpeedText.Text.Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ||
            speed <= 0 || speed > 100)
            throw new ArgumentException("Velocidade deve estar entre 0.001 e 100.");

        return new VLoteExportOptions
        {
            Width = ParseUInt(OutputWidth, 64, 4096, "Largura"),
            Height = ParseUInt(OutputHeight, 64, 4096, "Altura"),
            Fps = ParseUInt(FramesPerSecond, 1, 120, "FPS"),
            Crf = ParseInt(CrfText, 0, 63, "CRF"),
            Quality = ParseInt(QualityText, 0, 100, "Qualidade"),
            Speed = speed,
            Format = (FFmpegVideoExporter.VideoFormat)Format.SelectedItem!,
            AllAnimations = AllAnimations.IsChecked == true,
            Transparent = Transparent.IsChecked == true,
            AutoFit = AutoFit.IsChecked == true,
            KeepLast = KeepLast.IsChecked == true,
            Loop = Loop.IsChecked == true,
            Lossless = Lossless.IsChecked == true,
            DisableTrackLoop = DisableTrackLoop.IsChecked == true
        };
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ExportButton.IsEnabled = !busy && _active is not null;
        NullButton.IsEnabled = !busy && _queue?.Current is not null;
        CancelButton.IsEnabled = busy;
        RootPath.IsEnabled = !busy;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _queue?.Current is not VLoteEntry entry ||
            _vm is null || _active is null) return;

        VLoteExportOptions options;
        try { options = Options(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "VLote: configuração inválida",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var model = _active;
        var currentQueue = _queue;
        var exporter = _vm.SpineObjectListViewModel.FFmpegVideoExporterViewModel;
        var output = currentQueue.ExportDirectoryFor(entry);
        _cancel = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(text => Publish(text));
            Publish("Exportando... A pasta ainda NÃO foi movida.");
            await Task.Run(() => exporter.ExportVLote(model, output, options,
                message => ((IProgress<string>)progress).Report(message), _cancel.Token));

            // Release open textures/atlas handles BEFORE moving their folder.
            UnloadModels(false);
            var moved = currentQueue.Complete(entry, VLoteDecision.OK);
            Publish(moved);
            LoadNext();
        }
        catch (OperationCanceledException)
        {
            Publish("Exportação cancelada. O modelo permanece na fila e no local original.");
        }
        catch (Exception ex)
        {
            Publish($"FALHA: {ex.Message}. Nada foi movido. Corrija ou use NULL.");
            MessageBox.Show(ex.Message, "VLote: export falhou (arquivos preservados)",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            SetBusy(false);
        }
    }

    private void Null_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _queue?.Current is not VLoteEntry entry) return;
        try
        {
            UnloadModels(false);
            var moved = _queue.Complete(entry, VLoteDecision.Null);
            Publish(moved);
            LoadNext();
        }
        catch (Exception ex)
        {
            Publish("Não foi possível mover. Os arquivos originais permanecem na raiz.");
            MessageBox.Show(ex.Message, "VLote: falha ao classificar",
                MessageBoxButton.OK, MessageBoxImage.Error);
            LoadNext();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _cancel?.Cancel();
}
