using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RecNetPatcher.Core;

namespace RecNetPatcher.GUI;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions PresetJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private PatchMode _mode = PatchMode.Metadata;
    private string? _inputPath;

    public MainWindow()
    {
        InitializeComponent();
        ApplyMode();
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        _mode = sender switch
        {
            Button button when button == MetadataButton => PatchMode.Metadata,
            Button button when button == DllButton => PatchMode.Dll,
            _ => PatchMode.Photon,
        };

        HidePresets();
        ApplyMode();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowClipHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement root)
            return;

        root.Clip = new RectangleGeometry(
            new Rect(0, 0, root.ActualWidth, root.ActualHeight),
            12,
            12
        );
    }

    private void DropZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            CheckFileExists = true,
            Filter = GetInputFilter(),
            Title = "Select input file",
        };

        if (dialog.ShowDialog(this) == true)
            SetInput(dialog.FileName);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetInput(files[0]);
    }

    private void PatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_inputPath) || !File.Exists(_inputPath))
            return;

        SaveFileDialog dialog = new()
        {
            FileName = BuildDefaultOutputName(_inputPath),
            Filter = GetOutputFilter(),
            OverwritePrompt = true,
            Title = "Select output file",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            PatchButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            List<string> log = Patch(_inputPath, dialog.FileName);
        }
        catch (Exception ex)
            when (ex
                    is InvalidOperationException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException
            )
        { }
        finally
        {
            Mouse.OverrideCursor = null;
            PatchButton.IsEnabled = true;
        }
    }

    private List<string> Patch(string inputPath, string outputPath)
    {
        return _mode switch
        {
            PatchMode.Metadata => PatchMetadata(inputPath, outputPath, ReplacementUrlTextBox.Text),
            PatchMode.Dll => PatchDll(inputPath, outputPath, ReplacementUrlTextBox.Text),
            PatchMode.Photon => PatchPhoton(inputPath, outputPath),
            _ => throw new InvalidOperationException("Unknown patch mode."),
        };
    }

    private static List<string> PatchMetadata(
        string inputPath,
        string outputPath,
        string replacement
    )
    {
        return [.. Patcher.PatchMetadata(inputPath, outputPath, replacement)];
    }

    private static List<string> PatchDll(string inputPath, string outputPath, string replacement)
    {
        return [.. Patcher.PatchDll(inputPath, outputPath, replacement)];
    }

    private List<string> PatchPhoton(string inputPath, string outputPath)
    {
        string[] replacements = [PhotonTopTextBox.Text, PhotonBottomTextBox.Text];
        string[] originals = GetOriginalPhotonIds();
        return [.. Patcher.PatchPhoton(inputPath, outputPath, replacements, originals)];
    }

    private string[] GetOriginalPhotonIds()
    {
        string top = OriginalPhotonTopTextBox.Text.Trim();
        string bottom = OriginalPhotonBottomTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(top) && string.IsNullOrWhiteSpace(bottom))
            return [.. PatcherDefaults.RecRoomPhotonIds];

        if (string.IsNullOrWhiteSpace(top) || string.IsNullOrWhiteSpace(bottom))
            throw new InvalidOperationException(
                "Provide both original Photon IDs, or leave both original fields empty."
            );

        return [top, bottom];
    }
    private void ApplyMode()
    {
        UrlPanel.Visibility = _mode == PatchMode.Photon ? Visibility.Collapsed : Visibility.Visible;
        PhotonPanel.Visibility =
            _mode == PatchMode.Photon ? Visibility.Visible : Visibility.Collapsed;

        bool presetsVisible = PresetsPanel.Visibility == Visibility.Visible;
        SetModeButton(MetadataButton, !presetsVisible && _mode == PatchMode.Metadata);
        SetModeButton(DllButton, !presetsVisible && _mode == PatchMode.Dll);
        SetModeButton(PhotonButton, !presetsVisible && _mode == PatchMode.Photon);
        SetModeButton(PresetsButton, presetsVisible);

        if (_mode == PatchMode.Photon && Height < 645)
            Height = 645;
        else if (_mode != PatchMode.Photon || presetsVisible)
            MinHeight = 420;
        if (_mode == PatchMode.Photon)
            MinHeight = 645;

        if (string.IsNullOrWhiteSpace(_inputPath))
            ResetInputPrompt();
    }

    private static void SetModeButton(Button button, bool active)
    {
        button.Background = active
            ? (Brush)Application.Current.Resources["SelectedBrush"]
            : (Brush)Application.Current.Resources["PanelBrush"];
        button.Foreground = active
            ? (Brush)Application.Current.Resources["SelectedTextBrush"]
            : (Brush)Application.Current.Resources["TextBrush"];
    }

    private void SetInput(string path)
    {
        _inputPath = path;
        InputTitle.Text = Path.GetFileName(path);
        InputPathText.Text = path;
    }

    private void ResetInputPrompt()
    {
        InputTitle.Text = _mode switch
        {
            PatchMode.Metadata => "Drop global-metadata.dat here",
            PatchMode.Dll => "Drop Assembly-CSharp.dll here",
            PatchMode.Photon => "Drop resources.assets here",
            _ => "Drop input file here",
        };
        InputPathText.Text = "or click to browse";
    }

    private string GetInputFilter()
    {
        return _mode switch
        {
            PatchMode.Dll => "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
            PatchMode.Metadata => "Metadata files (*.dat;*.assets)|*.dat;*.bin|All files (*.*)|*.*",
            PatchMode.Photon => "Photon Files (*.assets;*.dat)|*.assets;*.bin|All files (*.*)|*.*",
            _ => "All files (*.*)|*.*",
        };
    }

    private string GetOutputFilter()
    {
        return _mode == PatchMode.Dll
            ? "DLL files (*.dll)|*.dll|All files (*.*)|*.*"
            : "All files (*.*)|*.*";
    }

    private static string BuildDefaultOutputName(string inputPath)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? "";
        string fileName = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);

        return Path.Combine(directory, $"{fileName}.patched{extension}");
    }

    private enum PatchMode
    {
        Metadata,
        Dll,
        Photon,
    }

    private void PresetsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPresets();
    }

    private void CreatePresetButton_Click(object sender, RoutedEventArgs e)
    {
        string presetName = PresetNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(presetName))
            return;

        Directory.CreateDirectory(GetPresetsDirectory());

        Preset preset = CreatePresetFromFields(presetName);
        string path = Path.Combine(
            GetPresetsDirectory(),
            $"{SanitizePresetFileName(presetName)}.json"
        );
        string json = JsonSerializer.Serialize(preset, PresetJsonOptions);

        File.WriteAllText(path, json);
        PresetNameTextBox.Clear();
        RefreshPresetList();
    }

    private void ShowPresets()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        PresetsPanel.Visibility = Visibility.Visible;
        RefreshPresetList();
        ApplyMode();
    }

    private void HidePresets()
    {
        EditorPanel.Visibility = Visibility.Visible;
        PresetsPanel.Visibility = Visibility.Collapsed;
    }

    private void RefreshPresetList()
    {
        PresetListPanel.Children.Clear();

        string presetsDirectory = GetPresetsDirectory();
        if (!Directory.Exists(presetsDirectory))
        {
            PresetListPanel.Children.Add(CreateEmptyPresetText());
            return;
        }

        string[] presetFiles =
        [
            .. Directory
                .GetFiles(presetsDirectory, "*.json")
                .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase),
        ];

        if (presetFiles.Length == 0)
        {
            PresetListPanel.Children.Add(CreateEmptyPresetText());
            return;
        }

        foreach (string presetFile in presetFiles)
        {
            Preset? preset = TryReadPreset(presetFile);
            if (preset is null)
                continue;

            PresetListPanel.Children.Add(CreatePresetRow(presetFile, preset));
        }

        if (PresetListPanel.Children.Count == 0)
            PresetListPanel.Children.Add(CreateEmptyPresetText());
    }

    private Border CreatePresetRow(string presetFile, Preset preset)
    {
        Border container = new()
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8),
            Background = (Brush)Application.Current.Resources["InputBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel textPanel = new();
        textPanel.Children.Add(
            new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(preset.Name)
                    ? Path.GetFileNameWithoutExtension(presetFile)
                    : preset.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
            }
        );

        Button loadButton = new()
        {
            Content = "Load",
            Width = 86,
            Height = 30,
            Style = (Style)Application.Current.Resources["ActionButtonStyle"],
            Tag = new CornerRadius(8),
        };
        loadButton.Click += (_, _) => LoadPreset(preset);

        Grid.SetColumn(textPanel, 0);
        Grid.SetColumn(loadButton, 1);
        row.Children.Add(textPanel);
        row.Children.Add(loadButton);

        container.Child = row;
        return container;
    }

    private static TextBlock CreateEmptyPresetText()
    {
        return new TextBlock
        {
            Text = "No presets found.",
            Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
            FontSize = 12,
        };
    }

    private void LoadPreset(Preset preset)
    {
        ReplacementUrlTextBox.Text = preset.ReplacementUrl;
        PhotonTopTextBox.Text = preset.ReplacementIds.Top;
        PhotonBottomTextBox.Text = preset.ReplacementIds.Bottom;
        OriginalPhotonTopTextBox.Text = preset.OriginalIds.Top;
        OriginalPhotonBottomTextBox.Text = preset.OriginalIds.Bottom;

        HidePresets();
        ApplyMode();
    }

    private Preset CreatePresetFromFields(string presetName)
    {
        return new Preset
        {
            Name = presetName,
            ReplacementUrl = ReplacementUrlTextBox.Text,
            ReplacementIds = new PhotonIds
            {
                Top = PhotonTopTextBox.Text,
                Bottom = PhotonBottomTextBox.Text,
            },
            OriginalIds = new PhotonIds
            {
                Top = OriginalPhotonTopTextBox.Text,
                Bottom = OriginalPhotonBottomTextBox.Text,
            },
        };
    }

    private static Preset? TryReadPreset(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), PresetJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string GetPresetsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Presets");
    }

    private static string SanitizePresetFileName(string presetName)
    {
        string sanitized = Sanitize().Replace(presetName, "_").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Preset" : sanitized;
    }

    private sealed class Preset
    {
        public string Name { get; set; } = "";
        public string ReplacementUrl { get; set; } = "https://ns.rec.net";
        public PhotonIds ReplacementIds { get; set; } = new();
        public PhotonIds OriginalIds { get; set; } = new();
    }

    private sealed class PhotonIds
    {
        public string Top { get; set; } = "";
        public string Bottom { get; set; } = "";
    }

    [GeneratedRegex("""[<>:""/\\|?*\x00-\x1F]""")]
    private static partial Regex Sanitize();
}
