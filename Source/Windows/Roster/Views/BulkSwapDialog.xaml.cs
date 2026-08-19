using System.Windows;

namespace Madden26Plugin.Roster.Views;

public partial class BulkSwapDialog : Window
{
    private readonly List<PlayerVisualRecipe> _players;

    public BulkSwapDialog(List<PlayerVisualRecipe> players)
    {
        InitializeComponent();
        _players = players;

        var allValues = players
            .SelectMany(p => p.Equipment.Values)
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        foreach (var val in allValues)
        {
            FindComboBox.Items.Add(val);
            ReplaceComboBox.Items.Add(val);
        }

        FindComboBox.SelectionChanged += (_, _) => UpdatePreview();
        ReplaceComboBox.SelectionChanged += (_, _) => UpdatePreview();
        ReplaceComboBox.LostFocus += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        var find = FindComboBox.Text;
        if (string.IsNullOrEmpty(find))
        {
            PreviewText.Text = "";
            return;
        }

        var count = _players.Count(p =>
            p.Equipment.Values.Any(v => string.Equals(v, find, StringComparison.OrdinalIgnoreCase)));

        PreviewText.Text = $"{count} player(s) have this equipment value.";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var find = FindComboBox.Text;
        var replace = ReplaceComboBox.Text;

        if (string.IsNullOrEmpty(find))
        {
            MessageBox.Show(this, "Please enter a value to find.", "Bulk Swap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changed = 0;
        foreach (var player in _players)
        {
            var slotsToUpdate = player.Equipment
                .Where(kv => string.Equals(kv.Value, find, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var slot in slotsToUpdate)
            {
                player.Equipment[slot] = replace;

                var entry = player.EquipmentEntries.FirstOrDefault(e => e.Slot == slot);
                if (entry != null)
                    entry.Value = replace;

                changed++;
            }
        }

        MessageBox.Show(this, $"Updated {changed} equipment slot(s) across {_players.Count} players.", "Bulk Swap Complete",
            MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
