using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Transl8r.Config;

namespace Transl8r.Ui;

/// <summary>
/// Tabbed settings dialog (ports settings.py). Edits a clone of the config; the
/// caller reads <see cref="Result"/> on OK and applies it. Audio/TTS fields are
/// present for config parity even though those pipelines aren't wired yet.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] OcrBackends = { "manga-ocr", "paddle", "vlm", "vlm-direct" };
    private static readonly string[] Translators = { "argos", "deepl", "server" };
    private static readonly string[] WhisperModels = { "tiny", "base", "small", "medium", "large-v3" };
    private static readonly string[] WhisperDevices = { "auto", "cuda", "cpu" };

    private readonly AppConfig _cfg;

    // Input
    private readonly CheckBox _ocrEnabled = new();
    private readonly TextBox _poll = new();
    private readonly TextBox _frameChange = new();
    private readonly ComboBox _ocrBackend = new();
    private readonly TextBox _paddleConf = new();
    private readonly TextBox _vlmUrl = new();
    private readonly TextBox _vlmModel = new();
    private readonly TextBox _hotkeyRegion = new();
    private readonly TextBox _hotkeyAdd = new();
    private readonly TextBox _hotkeyOverlay = new();
    private readonly TextBox _hotkeyEdit = new();
    private readonly CheckBox _audioEnabled = new();
    private readonly ComboBox _whisperModel = new();
    private readonly ComboBox _whisperDevice = new();
    private readonly CheckBox _audioVad = new();
    private readonly CheckBox _audioUseTranslator = new();

    // Translation
    private readonly ComboBox _translator = new();
    private readonly PasswordBox _deeplKey = new();
    private readonly TextBox _serverUrl = new();
    private readonly TextBox _serverModel = new();

    // Output
    private readonly CheckBox _outOverlay = new();
    private readonly CheckBox _showOriginal = new();
    private readonly TextBox _fontSize = new();
    private readonly TextBox _origFontSize = new();
    private readonly TextBox _opacity = new();
    private readonly TextBox _audioMsgSeconds = new();
    private readonly TextBox _audioMaxHeight = new();
    private readonly CheckBox _outTts = new();
    private readonly TextBox _ttsModel = new();
    private readonly TextBox _ttsVoices = new();
    private readonly TextBox _ttsVoice = new();
    private readonly CheckBox _outFile = new();
    private readonly TextBox _filePath = new();

    public AppConfig? Result { get; private set; }

    public SettingsWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;

        Tabs.Items.Add(new TabItem { Header = "Input", Content = Wrap(BuildInputTab()) });
        Tabs.Items.Add(new TabItem { Header = "Translation", Content = Wrap(BuildTranslationTab()) });
        Tabs.Items.Add(new TabItem { Header = "Output", Content = Wrap(BuildOutputTab()) });

        Load();
    }

    // ----------------------------------------------------------------- layout

    private static ScrollViewer Wrap(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MaxHeight = 560,
        Content = content,
    };

    private static Grid NewForm()
    {
        var g = new Grid { Margin = new Thickness(10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        return g;
    }

    private static void Row(Grid g, string label, FrameworkElement control, string? tip = null)
    {
        int r = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (tip != null)
        {
            lbl.ToolTip = tip;
            control.ToolTip = tip;
        }
        Grid.SetRow(lbl, r);
        Grid.SetColumn(lbl, 0);
        g.Children.Add(lbl);

        control.Margin = new Thickness(0, 4, 0, 4);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(control, r);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
    }

    private Grid BuildInputTab()
    {
        var g = NewForm();
        foreach (string b in OcrBackends) _ocrBackend.Items.Add(b);
        foreach (string m in WhisperModels) _whisperModel.Items.Add(m);
        foreach (string d in WhisperDevices) _whisperDevice.Items.Add(d);

        Row(g, "Screen OCR pipeline", _ocrEnabled);
        Row(g, "Poll interval (s)", _poll);
        Row(g, "Frame change threshold", _frameChange,
            "Fraction of pixels that must change before a frame is re-read (0.01 = 1%).");
        Row(g, "OCR backend", _ocrBackend,
            "C# build supports vlm-direct and vlm; manga-ocr/paddle fall back to vlm-direct.");
        Row(g, "Paddle min confidence (not wired)", _paddleConf,
            "Paddle OCR isn't ported to C#; this value is currently unused.");
        Row(g, "VLM URL (OpenAI-compat)", _vlmUrl);
        Row(g, "VLM model name", _vlmModel);
        Row(g, "Hotkey: pick region (replace)", _hotkeyRegion);
        Row(g, "Hotkey: add region", _hotkeyAdd);
        Row(g, "Hotkey: toggle overlay", _hotkeyOverlay);
        Row(g, "Hotkey: edit positions", _hotkeyEdit);
        Row(g, "Audio pipeline", _audioEnabled);
        Row(g, "Whisper model", _whisperModel);
        Row(g, "Whisper device (CPU only for now)", _whisperDevice,
            "GPU/CUDA runtime isn't wired yet; transcription runs on CPU regardless of this setting.");
        Row(g, "Voice activity detection (skip non-speech)", _audioVad,
            "Uses Silero VAD to drop chunks with no speech, preventing Whisper from " +
            "hallucinating captions on music/silence (\"thank you for watching\").");
        Row(g, "Whisper: transcribe, then translate separately", _audioUseTranslator,
            "ON: Whisper transcribes the Japanese, then your Translation backend "
            + "(DeepL/server) produces the English — usually better quality, and "
            + "required for 'Show original JA text' to work on audio. "
            + "OFF: Whisper translates the audio straight to English (faster, English only).");
        return g;
    }

    private Grid BuildTranslationTab()
    {
        var g = NewForm();
        foreach (string t in Translators) _translator.Items.Add(t);

        Row(g, "Backend", _translator,
            "argos isn't ported to C#; use deepl or server, or the vlm-direct OCR backend.");
        Row(g, "DeepL API key", _deeplKey);
        Row(g, "Server URL (OpenAI-compat)", _serverUrl);
        Row(g, "Server model name", _serverModel);
        return g;
    }

    private Grid BuildOutputTab()
    {
        var g = NewForm();
        Row(g, "Overlay", _outOverlay);
        Row(g, "Show original JA text", _showOriginal,
            "Shows the original Japanese above each translation. For audio output this "
            + "requires 'Whisper: transcribe, then translate separately' (Input tab) to be "
            + "on — otherwise Whisper outputs English directly and there is no JA transcript.");
        Row(g, "Translation font size", _fontSize);
        Row(g, "Original (JA) font size", _origFontSize,
            "Size of the original Japanese line. Only used when 'Show original JA text' is on.");
        Row(g, "Overlay background opacity", _opacity);
        Row(g, "Audio log: line lifetime (s, 0 = keep)", _audioMsgSeconds,
            "How long each audio subtitle line stays before it expires. 0 keeps lines until they scroll off the top.");
        Row(g, "Audio log: max height (% of screen)", _audioMaxHeight,
            "How tall the rolling audio overlay may grow before the oldest lines are dropped.");
        Row(g, "TTS (not wired)", _outTts,
            "Text-to-speech output is deferred (Phase 4); these fields have no effect yet.");
        Row(g, "TTS model path", _ttsModel);
        Row(g, "TTS voices path", _ttsVoices);
        Row(g, "TTS voice", _ttsVoice);
        Row(g, "Write to file", _outFile,
            "Append each translation to a log file: \"[HH:MM:SS] original => translation\". "
            + "Applies to both screen-OCR and audio output.");
        Row(g, "File path", _filePath,
            "Where to append. Relative paths are resolved from the app's working directory.");
        return g;
    }

    // ----------------------------------------------------------------- load/read

    private void Load()
    {
        _ocrEnabled.IsChecked = _cfg.OcrEnabled;
        _poll.Text = Str(_cfg.PollInterval);
        _frameChange.Text = Str(_cfg.FrameChangeRatio);
        _ocrBackend.SelectedItem = _cfg.OcrBackend;
        _paddleConf.Text = Str(_cfg.PaddleMinConfidence);
        _vlmUrl.Text = _cfg.VlmUrl;
        _vlmModel.Text = _cfg.VlmModel;
        _hotkeyRegion.Text = _cfg.HotkeyRegion;
        _hotkeyAdd.Text = _cfg.HotkeyAdd;
        _hotkeyOverlay.Text = _cfg.HotkeyOverlay;
        _hotkeyEdit.Text = _cfg.HotkeyEdit;
        _audioEnabled.IsChecked = _cfg.AudioEnabled;
        _whisperModel.SelectedItem = _cfg.WhisperModel;
        _whisperDevice.SelectedItem = _cfg.WhisperDevice;
        _audioVad.IsChecked = _cfg.AudioVad;
        _audioUseTranslator.IsChecked = _cfg.AudioUseTranslator;

        _translator.SelectedItem = _cfg.Translator;
        _deeplKey.Password = _cfg.DeeplApiKey;
        _serverUrl.Text = _cfg.ServerUrl;
        _serverModel.Text = _cfg.ServerModel;

        _outOverlay.IsChecked = _cfg.OutputOverlay;
        _showOriginal.IsChecked = _cfg.ShowOriginal;
        _fontSize.Text = Str(_cfg.OverlayFontSize);
        _origFontSize.Text = Str(_cfg.OverlayOrigFontSize);
        _origFontSize.IsEnabled = _cfg.ShowOriginal; // greyed out unless JA shown
        _showOriginal.Checked += (_, _) => _origFontSize.IsEnabled = true;
        _showOriginal.Unchecked += (_, _) => _origFontSize.IsEnabled = false;
        _opacity.Text = Str(_cfg.OverlayOpacity);
        _audioMsgSeconds.Text = Str(_cfg.AudioMessageSeconds);
        _audioMaxHeight.Text = _cfg.AudioOverlayMaxHeightPercent.ToString(CultureInfo.InvariantCulture);
        _outTts.IsChecked = _cfg.OutputTts;
        _ttsModel.Text = _cfg.TtsModelPath;
        _ttsVoices.Text = _cfg.TtsVoicesPath;
        _ttsVoice.Text = _cfg.TtsVoice;
        _outFile.IsChecked = _cfg.OutputFile;
        _filePath.Text = _cfg.OutputFilePath;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        AppConfig c = _cfg.Clone(); // preserves regions, offsets, Extra

        c.OcrEnabled = _ocrEnabled.IsChecked == true;
        c.PollInterval = Dbl(_poll.Text, c.PollInterval);
        c.FrameChangeRatio = Dbl(_frameChange.Text, c.FrameChangeRatio);
        c.OcrBackend = (string?)_ocrBackend.SelectedItem ?? c.OcrBackend;
        c.PaddleMinConfidence = Dbl(_paddleConf.Text, c.PaddleMinConfidence);
        c.VlmUrl = _vlmUrl.Text.Trim();
        c.VlmModel = _vlmModel.Text.Trim();
        c.HotkeyRegion = _hotkeyRegion.Text.Trim();
        c.HotkeyAdd = _hotkeyAdd.Text.Trim();
        c.HotkeyOverlay = _hotkeyOverlay.Text.Trim();
        c.HotkeyEdit = _hotkeyEdit.Text.Trim();
        c.AudioEnabled = _audioEnabled.IsChecked == true;
        c.WhisperModel = (string?)_whisperModel.SelectedItem ?? c.WhisperModel;
        c.WhisperDevice = (string?)_whisperDevice.SelectedItem ?? c.WhisperDevice;
        c.AudioVad = _audioVad.IsChecked == true;
        c.AudioUseTranslator = _audioUseTranslator.IsChecked == true;

        c.Translator = (string?)_translator.SelectedItem ?? c.Translator;
        c.DeeplApiKey = _deeplKey.Password.Trim();
        c.ServerUrl = _serverUrl.Text.Trim();
        c.ServerModel = _serverModel.Text.Trim();

        c.OutputOverlay = _outOverlay.IsChecked == true;
        c.ShowOriginal = _showOriginal.IsChecked == true;
        c.OverlayFontSize = (int)Dbl(_fontSize.Text, c.OverlayFontSize);
        c.OverlayOrigFontSize = (int)Dbl(_origFontSize.Text, c.OverlayOrigFontSize);
        c.OverlayOpacity = Dbl(_opacity.Text, c.OverlayOpacity);
        c.AudioMessageSeconds = Dbl(_audioMsgSeconds.Text, c.AudioMessageSeconds);
        c.AudioOverlayMaxHeightPercent = (int)Dbl(_audioMaxHeight.Text, c.AudioOverlayMaxHeightPercent);
        c.OutputTts = _outTts.IsChecked == true;
        c.TtsModelPath = _ttsModel.Text.Trim();
        c.TtsVoicesPath = _ttsVoices.Text.Trim();
        c.TtsVoice = _ttsVoice.Text.Trim();
        c.OutputFile = _outFile.IsChecked == true;
        c.OutputFilePath = _filePath.Text.Trim();

        Result = c;
        DialogResult = true;
    }

    private static string Str(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static double Dbl(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
