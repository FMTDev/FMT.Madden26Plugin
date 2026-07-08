using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Madden26Plugin.Roster.Views;

public partial class RosterEditorWindow : Window
{
    private RosterData _rosterData;
    private List<PlayerVisualRecipe> _allPlayers = new();
    private ObservableCollection<PlayerVisualRecipe> _filteredPlayers = new();
    private byte[] _originalFileBytes;
    private string _currentFilePath;
    private bool _isModified;
    private System.Timers.Timer _autoSaveTimer;
    private const int AutoSaveIntervalMs = 180_000;
    private readonly ComplexionPresetMapper _complexionMapper = new();
    private PlayerVisualRecipe _currentPlayer;
    private bool _isUpdatingUi;
    private Dictionary<string, List<string>> _slotValues = new();
    private FaceTemplateMatcher _faceMatcher;

    private static readonly string[] KnownPositions =
        { "QB","RB","FB","WR","TE","OT","OG","C","OL","LT","LG","RG","RT",
          "DL","DE","DT","NT","EDGE","LB","ILB","OLB","MLB","CB","DB","FS","SS","S","NB",
          "K","PK","P","LS","KR","PR","ATH","RET","ST" };

    private static readonly string[] KnownClassYears =
        { "Fr","RFr","So","RSo","Jr","RJr","Sr","RSr","GS" };

    public RosterEditorWindow()
    {
        InitializeComponent();
        PlayerListBox.ItemsSource = _filteredPlayers;
        UpdatePlayerCount();
        StartAutoSaveTimer();
        PositionCombo.ItemsSource = KnownPositions;
        ClassYearCombo.ItemsSource = KnownClassYears;
        SkinToneCombo.ItemsSource = Enum.GetValues<SkinToneGroup>();
        HairColorCombo.ItemsSource = HairColorMapper.AllHairColorDescriptions;
        EyeColorCombo.ItemsSource = HairColorMapper.AllEyeColorDescriptions;
    }

    private void StartAutoSaveTimer()
    {
        _autoSaveTimer = new System.Timers.Timer(AutoSaveIntervalMs);
        _autoSaveTimer.Elapsed += (_, _) =>
        {
            if (_isModified && !string.IsNullOrEmpty(_currentFilePath))
                Dispatcher.Invoke(() => AutoSave());
        };
        _autoSaveTimer.AutoReset = true;
        _autoSaveTimer.Start();
    }

    private string AutoSavePath =>
        !string.IsNullOrEmpty(_currentFilePath)
            ? _currentFilePath + ".autosave"
            : null;

    private void MarkModified()
    {
        if (_isModified) return;
        _isModified = true;
        Title = $"CFB27 Roster Editor - {Path.GetFileName(_currentFilePath)} (modified)";
    }

    private void MarkSaved()
    {
        _isModified = false;
        Title = $"CFB27 Roster Editor - {Path.GetFileName(_currentFilePath)}";
    }

    private void AutoSave()
    {
        var path = AutoSavePath;
        if (path == null || _rosterData == null) return;

        try
        {
            var writer = new CFB27RosterWriter();
            writer.WriteRosterFile(path, _rosterData, _originalFileBytes);
            var time = DateTime.Now.ToString("HH:mm:ss");
            StatusText.Text = $"Auto-saved to {Path.GetFileName(path)} at {time}";
        }
        catch
        {
            // silent — don't spam the user with auto-save errors
        }
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open CFB27 Roster File",
            Filter = "All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        LoadRoster(dialog.FileName);
    }

    public void LoadRoster(string filePath)
    {
        StatusText.Text = "Loading roster...";

        try
        {
            _originalFileBytes = File.ReadAllBytes(filePath);
            var reader = new CFB27RosterReader();
            _rosterData = reader.ReadRoster(_originalFileBytes);
            _allPlayers = _rosterData.Players;
            _currentFilePath = filePath;
            MarkSaved();

            ApplyFilter();

            ExportJsonMenuItem.IsEnabled = true;
            ExportCsvMenuItem.IsEnabled = true;
            SaveMenuItem.IsEnabled = true;
            SaveAsMenuItem.IsEnabled = true;
            BulkSwapMenuItem.IsEnabled = true;
            SummaryMenuItem.IsEnabled = true;
            GenerateMenuItem.IsEnabled = true;
            CloneSelectedFaceMenuItem.IsEnabled = true;
            CloneAllFacesMenuItem.IsEnabled = true;
            BuildToneMapMenuItem.IsEnabled = true;
            LoadToneMapMenuItem.IsEnabled = true;
            ExportToneMapMenuItem.IsEnabled = true;

            BuildSlotValues();

            var statsCount = _rosterData.StatsRecords?.Count ?? 0;
            Title = $"CFB27 Roster Editor - {Path.GetFileName(filePath)}";
            StatusText.Text = $"Loaded {_allPlayers.Count} players, {statsCount} stat records from {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load roster: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Ready";
        }
    }

    private void ApplyFilter()
    {
        var search = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";

        _filteredPlayers.Clear();
        foreach (var player in _allPlayers)
        {
            if (string.IsNullOrEmpty(search) ||
                player.DisplayName.ToLowerInvariant().Contains(search) ||
                (player.FullId?.ToLowerInvariant().Contains(search) == true) ||
                (player.UniqueId?.ToLowerInvariant().Contains(search) == true))
            {
                _filteredPlayers.Add(player);
            }
        }

        UpdatePlayerCount();
    }

    private void UpdatePlayerCount()
    {
        if (PlayerCountText != null)
            PlayerCountText.Text = $"{_filteredPlayers.Count} / {_allPlayers.Count} players";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void PlayerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerListBox.SelectedItem is PlayerVisualRecipe player)
        {
            ShowPlayerDetails(player);
        }
    }

    private void ShowPlayerDetails(PlayerVisualRecipe player)
    {
        _currentPlayer = player;
        _isUpdatingUi = true;

        FirstNameBox.Text = player.FirstName ?? "";
        LastNameBox.Text = player.LastName ?? "";
        JerseyBox.Text = player.JerseyNumber ?? "";
        PositionCombo.Text = player.Position ?? "";
        ClassYearCombo.Text = player.ClassYear ?? "";
        HeightBox.Text = player.DisplayHeight;
        WeightBox.Text = player.WeightOffset?.ToString() ?? "";
        FaceIdBox.Text = player.UniqueId ?? "";
        SkinToneCombo.Text = player.SkinTone != SkinToneGroup.Unknown ? player.SkinTone.ToString() : "";
        HairColorCombo.Text = player.HairColorDescription;
        EyeColorCombo.Text = player.EyeColorDescription;
        PlayerIdText.Text = player.FullId ?? "";

        var bodyTypes = _allPlayers
            .Select(p => p.BodyType)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .OrderBy(b => b.Replace("_BodyType", ""))
            .ToList();
        BodyTypeCombo.ItemsSource = bodyTypes;
        BodyTypeCombo.Text = player.BodyType ?? "";

        _faceMatcher = new FaceTemplateMatcher(_allPlayers);
        var faces = _faceMatcher.GetAvailableFaces(player.Position ?? "QB");
        FaceCountText.Text = $"{_faceMatcher.AvailableTemplateCount} templates, {faces.Count} available for {player.Position ?? "?"}";

        foreach (var entry in player.EquipmentEntries)
            entry.AvailableValues = _slotValues.TryGetValue(entry.Slot, out var vals)
                ? vals : new List<string> { entry.Value };
        EquipmentGrid.ItemsSource = player.EquipmentEntries;

        var statsLines = new List<string>();
        if (_rosterData?.StatsRecords != null)
        {
            foreach (var stat in _rosterData.StatsRecords)
            {
                if (stat.JerseyNumber.HasValue || stat.OverallRating.HasValue)
                {
                    var parts = new List<string>();
                    if (stat.JerseyNumber.HasValue) parts.Add($"#{stat.JerseyNumber}");
                    if (stat.OverallRating.HasValue) parts.Add($"OVR={stat.OverallRating}");
                    if (stat.Speed.HasValue) parts.Add($"SPD={stat.Speed}");
                    if (stat.Strength.HasValue) parts.Add($"STR={stat.Strength}");
                    if (stat.Awareness.HasValue) parts.Add($"AWR={stat.Awareness}");
                    if (stat.Agility.HasValue) parts.Add($"AGI={stat.Agility}");
                    if (stat.Acceleration.HasValue) parts.Add($"ACC={stat.Acceleration}");
                    statsLines.Add(string.Join(" ", parts));
                }
            }
        }
        StatsText.Text = statsLines.Count > 0 ? $"Stats records ({statsLines.Count} total):\n" + string.Join("\n", statsLines.Take(5)) + (statsLines.Count > 5 ? $"\n... and {statsLines.Count - 5} more" : "") : "";

        DetailPanel.IsEnabled = true;
        _isUpdatingUi = false;
    }

    private void FirstNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = FirstNameBox.Text.Trim();
        var oldVal = _currentPlayer.FirstName;
        if (oldVal == newVal) return;
        BinaryRecordHelper.ReplaceFieldValue(_currentPlayer, oldVal, newVal);
        _currentPlayer.FirstName = newVal;
        RefreshCurrentPlayer();
        MarkModified();
    }

    private void LastNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = LastNameBox.Text.Trim();
        var oldVal = _currentPlayer.LastName;
        if (oldVal == newVal) return;
        BinaryRecordHelper.ReplaceFieldValue(_currentPlayer, oldVal, newVal);
        _currentPlayer.LastName = newVal;
        RefreshCurrentPlayer();
        MarkModified();
    }

    private void JerseyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = JerseyBox.Text.Trim();
        var oldVal = _currentPlayer.JerseyNumber;
        if (oldVal == newVal) return;
        BinaryRecordHelper.ReplaceJerseyNumber(_currentPlayer, newVal);
        MarkModified();
    }

    private void PositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = PositionCombo.SelectedItem as string ?? PositionCombo.Text;
        if (string.IsNullOrEmpty(newVal)) return;
        BinaryRecordHelper.ReplacePosition(_currentPlayer, newVal);
        MarkModified();
    }

    private void ClassYearCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = ClassYearCombo.SelectedItem as string ?? ClassYearCombo.Text;
        if (string.IsNullOrEmpty(newVal)) return;
        BinaryRecordHelper.ReplaceClassYear(_currentPlayer, newVal);
        MarkModified();
    }

    private void BodyTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = BodyTypeCombo.SelectedItem as string ?? BodyTypeCombo.Text;
        if (string.IsNullOrEmpty(newVal)) return;
        BinaryRecordHelper.ReplaceBodyType(_currentPlayer, newVal);
        MarkModified();
    }

    private void SkinToneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        if (Enum.TryParse<SkinToneGroup>(SkinToneCombo.Text, out var tone) && tone != SkinToneGroup.Unknown)
        {
            _currentPlayer.SkinTone = tone;
            MarkModified();
        }
    }

    private void HairColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var desc = HairColorCombo.Text;
        if (string.IsNullOrEmpty(desc)) return;
        _currentPlayer.HairColorDescription = desc;
        _currentPlayer.HairColorRecipe = HairColorMapper.GetHairRecipeNameByDescription(desc);
        var idx = HairColorMapper.AllHairColorDescriptions.IndexOf(desc);
        _currentPlayer.HairColorIndex = idx >= 0 ? idx + 1 : 1;
        MarkModified();
    }

    private void EyeColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var desc = EyeColorCombo.Text;
        if (string.IsNullOrEmpty(desc)) return;
        _currentPlayer.EyeColorDescription = desc;
        _currentPlayer.EyeColorRecipe = HairColorMapper.GetEyeRecipeNameByDescription(desc);
        MarkModified();
    }

    private void AssignFaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        if (_faceMatcher == null) _faceMatcher = new FaceTemplateMatcher(_allPlayers);

        var faceId = _faceMatcher.PickTemplate(_currentPlayer.Position ?? "QB");
        if (string.IsNullOrEmpty(faceId))
        {
            StatusText.Text = "No face templates available.";
            return;
        }

        _isUpdatingUi = true;
        _currentPlayer.UniqueId = faceId;
        FaceIdBox.Text = faceId;

        // Update skin tone, hair from the picked generic ID
        var tone = _faceMatcher.GetSkinTone(faceId);
        if (tone != SkinToneGroup.Unknown)
            _currentPlayer.SkinTone = tone;
        SkinToneCombo.Text = _currentPlayer.SkinTone != SkinToneGroup.Unknown ? _currentPlayer.SkinTone.ToString() : "";

        var hcIndex = _faceMatcher.GetHairColorIndex(faceId);
        _currentPlayer.HairColorIndex = hcIndex;
        _currentPlayer.HairColorDescription = HairColorMapper.GetHairColorDescription(hcIndex);
        _currentPlayer.HairColorRecipe = HairColorMapper.GetHairColorRecipeName(hcIndex);
        HairColorCombo.Text = _currentPlayer.HairColorDescription;

        _isUpdatingUi = false;
        BinaryRecordHelper.ReplaceUniqueId(_currentPlayer, faceId);
        RefreshCurrentPlayer();
        MarkModified();
        StatusText.Text = $"Assigned face {faceId}";
    }

    private void FaceIdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        var newVal = FaceIdBox.Text.Trim();
        var oldVal = _currentPlayer.UniqueId;
        if (oldVal == newVal) return;
        BinaryRecordHelper.ReplaceUniqueId(_currentPlayer, newVal);
        MarkModified();
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;

        var text = HeightBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        var inches = TryParseHeight(text);
        if (!inches.HasValue || inches < 48 || inches > 96)
            return;

        if (_currentPlayer.HeightInches == inches.Value)
            return;

        var record = _currentPlayer.RawRecordData;
        if (record == null) return;

        var patched = (byte[])record.Clone();
        if (!CFB27RosterReader.ApplyHeightToRecord(patched, inches.Value))
            return;

        _currentPlayer.HeightInches = inches.Value;
        _currentPlayer.HeightByte = (byte)(inches.Value * 2 - 12);
        _currentPlayer.RawRecordData = patched;
        RefreshCurrentPlayer();
        MarkModified();
    }

    private void WeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;

        var text = WeightBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (!int.TryParse(text, out var val))
            return;

        if (_currentPlayer.WeightOffset == val)
            return;

        _currentPlayer.WeightOffset = val;
    }

    private static int? TryParseHeight(string text)
    {
        text = text.Trim().Replace(" ", "");

        if (int.TryParse(text, out var inches))
            return inches;

        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)['′](\d+)(?:[""″]|in)?$");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var feet) &&
            int.TryParse(match.Groups[2].Value, out var inch))
        {
            return feet * 12 + inch;
        }

        return null;
    }

    private void EquipmentGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (_isUpdatingUi || _currentPlayer == null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;

        if (e.Row.Item is EquipmentEntry entry)
        {
            var newVal = entry.Value ?? "";
            var oldVal = _currentPlayer.Equipment.GetValueOrDefault(entry.Slot);
            if (oldVal == newVal) return;
            BinaryRecordHelper.ReplaceEquipmentValue(_currentPlayer, entry.Slot, newVal);
            MarkModified();
        }
    }

    private void BuildSlotValues()
    {
        _slotValues.Clear();
        foreach (var player in _allPlayers)
        {
            foreach (var kv in player.Equipment)
            {
                if (!_slotValues.ContainsKey(kv.Key))
                    _slotValues[kv.Key] = new List<string>();
                if (!_slotValues[kv.Key].Contains(kv.Value))
                    _slotValues[kv.Key].Add(kv.Value);
            }
        }
        foreach (var key in _slotValues.Keys)
            _slotValues[key].Sort();
    }

    private void RefreshCurrentPlayer()
    {
        var idx = _filteredPlayers.IndexOf(_currentPlayer);
        if (idx >= 0)
        {
            _filteredPlayers.RemoveAt(idx);
            _filteredPlayers.Insert(idx, _currentPlayer);
        }
        PlayerIdText.Text = _currentPlayer.FullId ?? "";
    }

    private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            SaveAsMenuItem_Click(sender, e);
            return;
        }

        SaveRoster(_currentFilePath);
    }

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Roster As",
            Filter = "All Files|*.*",
            FileName = Path.GetFileName(_currentFilePath) ?? "roster.bin"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        SaveRoster(dialog.FileName);
    }

    private void SaveRoster(string path)
    {
        try
        {
            var writer = new CFB27RosterWriter();
            writer.WriteRosterFile(path, _rosterData, _originalFileBytes);
            _currentFilePath = path;
            MarkSaved();
            StatusText.Text = $"Saved to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportJsonMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export as JSON",
            Filter = "JSON Files|*.json",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_allPlayers, options);
            File.WriteAllText(dialog.FileName, json);
            StatusText.Text = $"Exported {_allPlayers.Count} players to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export as CSV",
            Filter = "CSV Files|*.csv",
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".csv"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName);

            var slots = _allPlayers
                .SelectMany(p => p.Equipment.Keys)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            writer.WriteLine("FirstName,LastName,FullId,UniqueId,BodyType," + string.Join(",", slots));

            foreach (var player in _allPlayers)
            {
                var values = new List<string>
                {
                    CsvEscape(player.FirstName),
                    CsvEscape(player.LastName),
                    CsvEscape(player.FullId),
                    CsvEscape(player.UniqueId),
                    CsvEscape(player.BodyType)
                };

                values.AddRange(slots.Select(s => CsvEscape(player.Equipment.GetValueOrDefault(s))));
                writer.WriteLine(string.Join(",", values));
            }

            StatusText.Text = $"Exported {_allPlayers.Count} players to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private void BulkSwapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BulkSwapDialog(_allPlayers);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            ApplyFilter();
            MarkModified();
            AutoSave();
            StatusText.Text = "Bulk swap applied. Auto-saved.";
        }
    }

    private void SummaryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var bodyTypes = _allPlayers
            .GroupBy(p => p.BodyType ?? "(none)")
            .Select(g => $"{g.Key.Replace("_BodyType", "")}: {g.Count()}")
            .ToList();

        var gearUsage = _allPlayers
            .SelectMany(p => p.Equipment.Values)
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .Take(20)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        var msg = $"Total Players: {_allPlayers.Count}\n" +
                  $"Stat Records: {_rosterData?.StatsRecords?.Count ?? 0}\n\n" +
                  $"Body Types:\n  " + string.Join("\n  ", bodyTypes) + "\n\n" +
                  $"Top 20 Equipment Items:\n  " + string.Join("\n  ", gearUsage);

        MessageBox.Show(this, msg, "Roster Summary", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void GenerateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_rosterData == null) return;

        var templateMatcher = new FaceTemplateMatcher(_allPlayers);
        var dialog = new GenerateRosterDialog(_rosterData, templateMatcher)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyFilter();
            MarkModified();
            AutoSave();
            StatusText.Text = "Generated roster applied. Auto-saved.";
        }
    }

    private void CloneSelectedFaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerListBox.SelectedItem is not PlayerVisualRecipe player || !player.IsGenericPlayer)
        {
            MessageBox.Show(this, "Select a player with a Generic_ face ID first.", "No Selection");
            return;
        }
        CloneFace(player);
    }

    private void CloneAllFacesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var genericPlayers = _allPlayers.Where(p => p.IsGenericPlayer).ToList();
        if (genericPlayers.Count == 0)
        {
            MessageBox.Show(this, "No players with Generic_ face IDs found.", "Nothing to Clone");
            return;
        }

        var result = MessageBox.Show(this,
            $"Clone all {genericPlayers.Count} Generic_ faces to Unique_?\n\n" +
            "This will create new EBX recipe cards in FMT's asset manager. " +
            "You must save a .fbmod in FMT for these to persist.",
            "Clone All Faces", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var success = 0;
        var fail = 0;
        foreach (var p in genericPlayers)
        {
            if (CloneFace(p)) success++; else fail++;
        }

        ApplyFilter();
        MarkModified();
        StatusText.Text = $"Cloned {success} faces{(fail > 0 ? $", {fail} failed" : "")}. Save roster + save fbmod in FMT.";
    }

    private bool CloneFace(PlayerVisualRecipe player)
    {
        try
        {
            var cloner = new CyberfaceCloner();
            var genericName = player.UniqueId;
            var uniqueName = $"Generated_{Guid.NewGuid():N}"[..28];

            var hairRecipe = !string.IsNullOrEmpty(player.HairColorRecipe)
                ? player.HairColorRecipe : null;
            var eyeRecipe = !string.IsNullOrEmpty(player.EyeColorRecipe)
                ? player.EyeColorRecipe : null;

            if (!cloner.CloneGenericToUnique(genericName, uniqueName, hairRecipe, eyeRecipe))
                return false;

            BinaryRecordHelper.ReplaceUniqueId(player, uniqueName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BuildToneMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _complexionMapper.BuildFromFmt();
            var count = _complexionMapper.MappedCount;
            if (count == 0)
            {
                StatusText.Text = "No face tone mappings found. Run this inside FMT with a loaded profile.";
                return;
            }

            foreach (var player in _allPlayers.Where(p => p.IsGenericPlayer))
            {
                var tone = _complexionMapper.GetGroup(player.UniqueId);
                if (tone != SkinToneGroup.Unknown)
                    player.SkinTone = tone;
            }

            StatusText.Text = $"Built tone map: {count} Generic_ templates classified.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Tone map build failed: {ex.Message}";
        }
    }

    private void LoadToneMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Face Tone Map",
            Filter = "JSON Files|*.json",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            _complexionMapper.DeserializeMapping(json);

            foreach (var player in _allPlayers.Where(p => p.IsGenericPlayer))
            {
                var tone = _complexionMapper.GetGroup(player.UniqueId);
                if (tone != SkinToneGroup.Unknown)
                    player.SkinTone = tone;
            }

            StatusText.Text = $"Loaded tone map with {_complexionMapper.MappedCount} entries.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load tone map: {ex.Message}", "Error");
        }
    }

    private void ExportToneMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_complexionMapper.MappedCount == 0)
        {
            MessageBox.Show(this, "No face tone map data to export. Build or load one first.", "No Data");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Face Tone Map",
            Filter = "JSON Files|*.json",
            FileName = "face_tone_map.json",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var json = _complexionMapper.SerializeMapping();
            File.WriteAllText(dialog.FileName, json);
            StatusText.Text = $"Exported {_complexionMapper.MappedCount} tone map entries.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error");
        }
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isModified)
        {
            var result = MessageBox.Show(this,
                "You have unsaved changes. Auto-save before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                AutoSave();
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
        }

        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        base.OnClosing(e);
    }
}
