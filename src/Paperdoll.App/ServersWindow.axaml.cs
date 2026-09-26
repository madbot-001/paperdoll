using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperdoll.Core.Servers;

namespace Paperdoll.App;

public sealed record ServerChoice(string Name, string Address);

public sealed record ServerRow(string Name, string Players, string Address);

/// <summary>Picks a server from the hub's list, or by address.</summary>
public partial class ServersWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private List<ServerRow> _rows = [];

    public ServersWindow()
    {
        InitializeComponent();
        FilterBox.TextChanged += (_, _) => Filter();
        AddressBox.TextChanged += (_, _) => UpdateButton();
        ServerGrid.SelectionChanged += (_, _) => UpdateButton();
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Getting the server list";
        try
        {
            var servers = await new GameServers(Http).ListAsync();
            _rows = servers.Select(s => new ServerRow(s.Name, s.MaxPlayers is { } max ? $"{s.Players}/{max}" : s.Players.ToString(), s.Address)).ToList();
            StatusText.Text = $"{_rows.Count} servers on the hub.";
            Filter();
        }
        catch (Exception e)
        {
            StatusText.Text = $"Could not get the server list ({e.Message}). You can still type an address.";
        }
    }

    private void Filter()
    {
        var text = FilterBox.Text?.Trim() ?? "";
        ServerGrid.ItemsSource = _rows.Where(r => text.Length == 0 || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void UpdateButton() =>
        MatchButton.IsEnabled = ServerGrid.SelectedItem is ServerRow || !string.IsNullOrWhiteSpace(AddressBox.Text);

    private void OnMatch(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            var address = AddressBox.Text.Trim();
            try
            {
                GameServers.InfoUri(address);
            }
            catch (FormatException ex)
            {
                StatusText.Text = ex.Message;
                return;
            }
            Close(new ServerChoice(address, address));
        }
        else if (ServerGrid.SelectedItem is ServerRow row)
            Close(new ServerChoice(row.Name, row.Address));
    }

    private void OnServerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ServerGrid.SelectedItem is ServerRow row)
            Close(new ServerChoice(row.Name, row.Address));
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(null);
}
