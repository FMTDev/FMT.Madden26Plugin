using System.Windows;
using System.Windows.Controls;

namespace Madden26Plugin.Roster.Views;

public partial class AssignTeamDialog : Window
{
    private readonly List<PlayerVisualRecipe> _targets;

    public string SelectedTeam { get; private set; }

    public AssignTeamDialog(List<PlayerVisualRecipe> targets)
    {
        InitializeComponent();
        _targets = targets;

        HeaderText.Text = targets.Count == 1
            ? $"Assign team for {targets[0].DisplayName}:"
            : $"Assign team for {targets.Count} players:";

        ConferenceCombo.ItemsSource = TeamMap.AllConferences;
        ConferenceCombo.Text = TeamMap.AllConferences.FirstOrDefault() ?? "";
        TeamCombo.ItemsSource = TeamMap.AllTeamNames;
    }

    private void ConferenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var conf = ConferenceCombo.SelectedItem as string ?? ConferenceCombo.Text;
        if (string.IsNullOrEmpty(conf) || conf == "Unassigned")
            TeamCombo.ItemsSource = TeamMap.AllTeamNames;
        else
            TeamCombo.ItemsSource = TeamMap.GetTeams(conf);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedTeam = TeamCombo.Text;
        if (string.IsNullOrEmpty(SelectedTeam))
        {
            MessageBox.Show(this, "Please select a team.", "Assign Team", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
