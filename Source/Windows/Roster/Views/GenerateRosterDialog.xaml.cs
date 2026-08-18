using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Madden26Plugin.Roster.Views;

public partial class GenerateRosterDialog : Window
{
    private readonly RosterData _rosterData;
    private readonly FaceTemplateMatcher _templateMatcher;
    private readonly ObservableCollection<EditablePlayer> _previewPlayers = new();
    private List<NcaaPlayerInfo> _scrapedPlayers = new();
    private bool _isPopulatingCombo;
    private string _lastScrapeSource = "";
    private List<string> _allBodyTypes = new();

    private static readonly string[] KnownPositions =
        { "QB","RB","FB","WR","TE","OT","OG","C","OL","LT","LG","RG","RT",
          "DL","DE","DT","NT","EDGE","LB","ILB","OLB","MLB","CB","DB","FS","SS","S","NB",
          "K","PK","P","LS","KR","PR","ATH","RET","ST" };

    private static readonly string[] KnownClassYears =
        { "Fr","RFr","So","RSo","Jr","RJr","Sr","RSr","GS" };

    public GenerateRosterDialog(RosterData rosterData, FaceTemplateMatcher templateMatcher)
    {
        InitializeComponent();
        _rosterData = rosterData;
        _templateMatcher = templateMatcher;
        _allBodyTypes = rosterData.Players
            .Select(p => p.BodyType)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .OrderBy(b => b.Replace("_BodyType", ""))
            .ToList();
        PlayerGrid.ItemsSource = _previewPlayers;
        TemplateCountText.Text = $"{templateMatcher.AvailableTemplateCount} face templates available";
        PopulateTeamCombo();
    }

    private void PopulateTeamCombo()
    {
        _isPopulatingCombo = true;
        TeamCombo.ItemsSource = SidearmTeamMap.TeamNames;
        TeamCombo.SelectedIndex = TeamCombo.Items.IndexOf("Alabama");
        if (TeamCombo.SelectedIndex < 0 && TeamCombo.Items.Count > 0)
            TeamCombo.SelectedIndex = 0;
        _isPopulatingCombo = false;
    }

    private void TeamCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isPopulatingCombo) return;
        TeamCombo.IsDropDownOpen = TeamCombo.Text.Length > 0;
    }

    private async void FetchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(YearBox.Text.Trim(), out var year) || year < 2000 || year > 2030)
        {
            MessageBox.Show(this, "Enter a valid year (2000-2030).", "Invalid Year");
            return;
        }

        var customUrl = CustomUrlBox.Text?.Trim();
        var teamName = TeamCombo.Text?.Trim();

        if (string.IsNullOrEmpty(customUrl) && string.IsNullOrEmpty(teamName))
        {
            MessageBox.Show(this, "Select a team or enter a custom roster URL.", "Nothing to Fetch");
            return;
        }

        FetchButton.IsEnabled = false;
        FetchProgress.Visibility = Visibility.Visible;

        try
        {
            List<NcaaPlayerInfo> scraped;

            if (!string.IsNullOrEmpty(customUrl))
            {
                StatusLabel.Text = "Fetching from custom URL (Sidearm)...";
                using var sidearm = new SidearmSportsScraper();
                scraped = await sidearm.FetchTeamRosterFromUrlAsync(customUrl);

                if (scraped.Count == 0)
                {
                    StatusLabel.Text = "0 from Sidearm, trying On3...";
                    using var on3 = new On3Scraper();
                    scraped = await on3.FetchRosterFromUrlAsync(customUrl);
                }
            }
            else if (SidearmTeamMap.TryGetDomain(teamName, out var domain))
            {
                StatusLabel.Text = $"Fetching {teamName} {year} (Sidearm)...";
                using var sidearm = new SidearmSportsScraper();
                scraped = await sidearm.FetchTeamRosterAsync(domain, year);

                if (scraped.Count == 0 && On3TeamMap.TryGetSlug(teamName, out var slug))
                {
                    StatusLabel.Text = $"0 from Sidearm, trying On3 ({slug})...";
                    using var on3 = new On3Scraper();
                    scraped = await on3.FetchTeamRosterAsync(slug, year);
                }
            }
            else if (On3TeamMap.TryGetSlug(teamName, out var slug))
            {
                StatusLabel.Text = $"Fetching {teamName} {year} from On3...";
                using var on3 = new On3Scraper();
                scraped = await on3.FetchTeamRosterAsync(slug, year);
            }
            else
            {
                MessageBox.Show(this,
                    $"Unknown team '{teamName}'. Enter a custom roster URL, or check the team name.",
                    "Unknown Team", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scrapedPlayers = scraped;
            _lastScrapeSource = teamName;
            PopulatePreview(scraped);
            StatusLabel.Text = $"Fetched {scraped.Count} players. Edit fields, then click Apply.";
            ApplyButton.IsEnabled = scraped.Count > 0;
            RepickFacesButton.IsEnabled = scraped.Count > 0;
            SaveScrapeButton.IsEnabled = scraped.Count > 0;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            MessageBox.Show(this, $"Failed to fetch roster: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            FetchButton.IsEnabled = true;
            FetchProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulatePreview(List<NcaaPlayerInfo> scraped)
    {
        _previewPlayers.Clear();
        foreach (var p in scraped)
        {
            var (first, last) = SplitName(p.Name);
            var faceId = _templateMatcher.PickTemplate(p.Position);
            var tone = string.IsNullOrEmpty(faceId) ? SkinToneGroup.Unknown
                : _templateMatcher.GetSkinTone(faceId);
            var availableFaces = _templateMatcher.GetAvailableFaces(p.Position);
            var skinTones = Enum.GetNames<SkinToneGroup>()
                .Where(n => n != nameof(SkinToneGroup.Unknown))
                .ToList();
            _previewPlayers.Add(new EditablePlayer
            {
                FirstName = first,
                LastName = last,
                Jersey = p.JerseyNumber?.ToString() ?? "",
                Position = p.Position ?? "?",
                ClassYear = p.ClassYear ?? "?",
                BodyType = "",
                FaceId = faceId,
                SkinTone = tone == SkinToneGroup.Unknown ? "" : tone.ToString(),
                HairColor = _templateMatcher.GetHairColorDescription(faceId),
                EyeColor = "Brown",
                AvailableFaces = availableFaces,
                AvailablePositions = KnownPositions.ToList(),
                AvailableClassYears = KnownClassYears.ToList(),
                AvailableBodyTypes = _allBodyTypes,
                AvailableHairColors = HairColorMapper.AllHairColorDescriptions,
                AvailableEyeColors = HairColorMapper.AllEyeColorDescriptions,
                AvailableSkinTones = skinTones,
                HeightInches = (int)(p.HeightInches ?? 0),
                WeightLbs = p.Weight ?? 0,
            });
        }
    }

    private void RepickFacesButton_Click(object sender, RoutedEventArgs e)
    {
        var faceMatcher = new FaceTemplateMatcher(_rosterData.Players);
        foreach (var player in _previewPlayers)
        {
            player.AvailableFaces = faceMatcher.GetAvailableFaces(player.Position);
            player.FaceId = faceMatcher.PickTemplate(player.Position);
            var tone = faceMatcher.GetSkinTone(player.FaceId);
            player.SkinTone = tone == SkinToneGroup.Unknown ? "" : tone.ToString();
            player.HairColor = faceMatcher.GetHairColorDescription(player.FaceId);
            player.AvailableSkinTones = Enum.GetNames<SkinToneGroup>()
                .Where(n => n != nameof(SkinToneGroup.Unknown)).ToList();
            player.AvailableHairColors = HairColorMapper.AllHairColorDescriptions;
            player.AvailableBodyTypes = _allBodyTypes;
        }
        StatusLabel.Text = $"Re-picked face IDs for {_previewPlayers.Count} players. ({faceMatcher.AvailableTemplateCount} templates)";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPlayers.Count == 0) return;

        var faceMatcher = new FaceTemplateMatcher(_rosterData.Players);
        var replaceCount = Math.Min(_previewPlayers.Count, _rosterData.Players.Count);

        for (var i = 0; i < replaceCount; i++)
        {
            var editPlayer = _previewPlayers[i];
            var rosterPlayer = _rosterData.Players[i];

            var faceId = string.IsNullOrEmpty(editPlayer.FaceId)
                ? faceMatcher.PickTemplate(editPlayer.Position)
                : editPlayer.FaceId;

            BinaryRecordHelper.RebuildPlayerRecord(rosterPlayer, editPlayer.FirstName, editPlayer.LastName, faceId);

            if (editPlayer.HeightInches > 0)
            {
                rosterPlayer.HeightInches = editPlayer.HeightInches;
                rosterPlayer.HeightByte = (byte)(editPlayer.HeightInches * 2 - 12);
                if (rosterPlayer.RawRecordData != null)
                {
                    var patched = (byte[])rosterPlayer.RawRecordData.Clone();
                    if (CFB27RosterReader.ApplyHeightToRecord(patched, editPlayer.HeightInches))
                        rosterPlayer.RawRecordData = patched;
                }
            }

            if (editPlayer.WeightLbs > 0)
                rosterPlayer.WeightOffset = editPlayer.WeightLbs - 160;

            // Copy hair/eye color info for later use by CyberfaceCloner
            var hcIndex = HairColorMapper.ExtractHairColorIndex(faceId);
            rosterPlayer.HairColorIndex = hcIndex;
            rosterPlayer.HairColorRecipe = HairColorMapper.GetHairColorRecipeName(hcIndex);
            rosterPlayer.HairColorDescription = HairColorMapper.GetHairColorDescription(hcIndex);

            var eyeDesc = editPlayer.EyeColor;
            rosterPlayer.EyeColorRecipe = HairColorMapper.GetEyeRecipeNameByDescription(eyeDesc);
            rosterPlayer.EyeColorDescription = eyeDesc;

            if (int.TryParse(editPlayer.Jersey, out var jersey))
            {
                var statsRecord = _rosterData.StatsRecords
                    .FirstOrDefault(s => s.StreamIndex == rosterPlayer.StreamIndex);
                if (statsRecord != null)
                    statsRecord.JerseyNumber = jersey;
            }
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveScrapeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scrapedPlayers.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Scraped Roster Data",
            Filter = "JSON Files|*.json",
            FileName = string.IsNullOrEmpty(_lastScrapeSource)
                ? "roster_data.json"
                : $"{_lastScrapeSource.Replace(" ", "_").ToLowerInvariant()}_roster.json"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var json = JsonSerializer.Serialize(_scrapedPlayers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            StatusLabel.Text = $"Saved {_scrapedPlayers.Count} players to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load Scraped Roster Data",
            Filter = "JSON Files|*.json",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var loaded = JsonSerializer.Deserialize<List<NcaaPlayerInfo>>(json);
            if (loaded == null || loaded.Count == 0)
            {
                MessageBox.Show(this, "No players found in file.", "Empty Data");
                return;
            }

            _scrapedPlayers = loaded;
            _lastScrapeSource = Path.GetFileNameWithoutExtension(dialog.FileName);
            PopulatePreview(loaded);
            StatusLabel.Text = $"Loaded {loaded.Count} players from {Path.GetFileName(dialog.FileName)}. Edit fields, then click Apply.";
            ApplyButton.IsEnabled = loaded.Count > 0;
            RepickFacesButton.IsEnabled = loaded.Count > 0;
            SaveScrapeButton.IsEnabled = loaded.Count > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static (string first, string last) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return ("Player", "Unknown");

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return (parts[0], "");
        if (parts.Length == 2)
            return (parts[0], parts[1]);

        return (parts[0], string.Join(" ", parts.Skip(1)));
    }

}

public class EditablePlayer : INotifyPropertyChanged
{
    private string _firstName;
    private string _lastName;
    private string _jersey;
    private string _position;
    private string _classYear;
    private string _bodyType;
    private string _faceId;
    private string _skinTone;
    private string _hairColor;
    private string _eyeColor;
    private List<string> _availableFaces = new();
    private int _heightInches;
    private int _weightLbs;

    public string FirstName { get => _firstName; set => SetField(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetField(ref _lastName, value); }
    public string Jersey { get => _jersey; set => SetField(ref _jersey, value); }
    public string Position { get => _position; set => SetField(ref _position, value); }
    public string ClassYear { get => _classYear; set => SetField(ref _classYear, value); }
    public string BodyType { get => _bodyType; set => SetField(ref _bodyType, value); }
    public string FaceId { get => _faceId; set => SetField(ref _faceId, value); }
    public string SkinTone { get => _skinTone; set => SetField(ref _skinTone, value); }
    public string HairColor { get => _hairColor; set => SetField(ref _hairColor, value); }
    public string EyeColor { get => _eyeColor; set => SetField(ref _eyeColor, value); }
    public List<string> AvailableFaces { get => _availableFaces; set => SetField(ref _availableFaces, value); }
    public int HeightInches { get => _heightInches; set => SetField(ref _heightInches, value); }
    public int WeightLbs { get => _weightLbs; set => SetField(ref _weightLbs, value); }
    public string DisplayHeight => HeightInches > 0 ? $"{HeightInches / 12}'{HeightInches % 12}\"" : "";
    private List<string> _availableEyeColors = new();
    public List<string> AvailableEyeColors { get => _availableEyeColors; set => SetField(ref _availableEyeColors, value); }
    private List<string> _availableSkinTones = new();
    public List<string> AvailableSkinTones { get => _availableSkinTones; set => SetField(ref _availableSkinTones, value); }
    private List<string> _availablePositions = new();
    public List<string> AvailablePositions { get => _availablePositions; set => SetField(ref _availablePositions, value); }
    private List<string> _availableClassYears = new();
    public List<string> AvailableClassYears { get => _availableClassYears; set => SetField(ref _availableClassYears, value); }
    private List<string> _availableBodyTypes = new();
    public List<string> AvailableBodyTypes { get => _availableBodyTypes; set => SetField(ref _availableBodyTypes, value); }
    private List<string> _availableHairColors = new();
    public List<string> AvailableHairColors { get => _availableHairColors; set => SetField(ref _availableHairColors, value); }

    public event PropertyChangedEventHandler PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
