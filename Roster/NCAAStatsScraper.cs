using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Madden26Plugin.Roster;

public class NcaaPlayerInfo
{
    public string Name { get; set; }
    public string Position { get; set; }
    public string ClassYear { get; set; }
    public int? JerseyNumber { get; set; }
    public string Team { get; set; }
    public int? PassingYards { get; set; }
    public int? RushingYards { get; set; }
    public int? ReceivingYards { get; set; }
    public int? Touchdowns { get; set; }
    public int? Tackles { get; set; }
    public int? Interceptions { get; set; }
    public int? Sacks { get; set; }
    public double? HeightInches { get; set; }
    public int? Weight { get; set; }
}

public class NCAAStatsScraper : IDisposable
{
    private readonly HttpClient _httpClient;

    public NCAAStatsScraper()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    }

    public async Task<List<NcaaPlayerInfo>> FetchTeamRosterAsync(string teamSlug, int year)
    {
        var url = $"https://www.ncaa.com/sports/football/fbs/teams/{teamSlug}/roster/{year}";
        var html = await FetchPageAsync(url);
        return ParseRosterHtml(html, teamSlug);
    }

    public async Task<List<NcaaPlayerInfo>> FetchStatsPageAsync(string statCategory, int year)
    {
        var url = $"https://www.ncaa.com/stats/football/fbs/{year}/individual/{statCategory}";
        var html = await FetchPageAsync(url);
        return ParseStatsHtml(html);
    }

    private async Task<string> FetchPageAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    internal static List<NcaaPlayerInfo> ParseRosterHtml(string html, string teamSlug)
    {
        var players = new List<NcaaPlayerInfo>();
        var tables = ExtractTables(html);

        foreach (var table in tables)
        {
            var rows = ExtractRows(table);
            foreach (var row in rows)
            {
                var cells = ExtractCells(row);
                if (cells.Count < 3)
                    continue;

                var player = new NcaaPlayerInfo { Team = teamSlug };

                var nameLink = ExtractFirstLink(cells[0]);
                if (nameLink != null)
                {
                    player.Name = WebUtility.HtmlDecode(nameLink.Value.name);
                }
                else
                {
                    player.Name = WebUtility.HtmlDecode(StripHtml(cells[0]));
                }

                for (var i = 0; i < cells.Count; i++)
                {
                    var text = StripHtml(cells[i]).Trim();
                    if (i == 0 && player.Name == null)
                        player.Name = text;
                    else if (text.Length <= 3 && Regex.IsMatch(text, @"^\d{1,3}$"))
                        player.JerseyNumber = int.Parse(text);
                    else if (text is "QB" or "RB" or "WR" or "TE" or "OL" or "DL" or "LB" or "DB" or
                             "K" or "P" or "LS" or "ATH" or "FB" or "DE" or "DT" or "NT" or "CB" or "S" or
                             "ILB" or "OLB" or "C" or "G" or "T" or "OT")
                        player.Position = text;
                    else if (text is "Fr" or "So" or "Jr" or "Sr" or "R-Fr" or "R-So" or "R-Jr" or "R-Sr" or
                             "Freshman" or "Sophomore" or "Junior" or "Senior" or "Redshirt Freshman" or
                             "Redshirt Sophomore" or "Redshirt Junior" or "Redshirt Senior" or "Graduate" or "GS")
                        player.ClassYear = NormalizeClassYear(text);
                }

                if (!string.IsNullOrEmpty(player.Name))
                    players.Add(player);
            }
        }

        return players;
    }

    internal static List<NcaaPlayerInfo> ParseStatsHtml(string html)
    {
        var players = new List<NcaaPlayerInfo>();
        var tables = ExtractTables(html);

        foreach (var table in tables)
        {
            var rows = ExtractRows(table);
            foreach (var row in rows)
            {
                var cells = ExtractCells(row);
                if (cells.Count < 5)
                    continue;

                var player = new NcaaPlayerInfo();

                var nameLink = ExtractFirstLink(cells[1]);
                if (nameLink != null)
                    player.Name = WebUtility.HtmlDecode(nameLink.Value.name);
                else
                    player.Name = WebUtility.HtmlDecode(StripHtml(cells[1]));

                if (string.IsNullOrEmpty(player.Name))
                    continue;

                var teamText = StripHtml(cells[2]).Trim();
                player.Team = teamText;

                var statValues = new List<int?>();
                for (var i = 3; i < cells.Count; i++)
                {
                    var text = StripHtml(cells[i]).Trim();
                    if (int.TryParse(text, out var val))
                        statValues.Add(val);
                    else
                        statValues.Add(null);
                }

                if (statValues.Count >= 1) player.Touchdowns = statValues[0];

                players.Add(player);
            }
        }

        return players;
    }

    internal static List<string> ExtractTables(string html)
    {
        var tables = new List<string>();
        var matches = Regex.Matches(html, @"<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match match in matches)
            tables.Add(match.Value);
        return tables;
    }

    internal static List<string> ExtractRows(string tableHtml)
    {
        var rows = new List<string>();
        var matches = Regex.Matches(tableHtml, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match match in matches)
            rows.Add(match.Value);
        return rows;
    }

    internal static List<string> ExtractCells(string rowHtml)
    {
        var cells = new List<string>();
        var matches = Regex.Matches(rowHtml, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match match in matches)
            cells.Add(match.Value);
        return cells;
    }

    internal static string StripHtml(string html)
    {
        return Regex.Replace(html, @"<[^>]+>", "").Trim();
    }

    internal static (string name, string href)? ExtractFirstLink(string cellHtml)
    {
        var match = Regex.Match(cellHtml, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
            return (WebUtility.HtmlDecode(StripHtml(match.Groups[2].Value)), match.Groups[1].Value);
        return null;
    }

    internal static string NormalizeClassYear(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "fr" or "freshman" or "r-fr" or "redshirt freshman" => "Fr",
            "so" or "sophomore" or "r-so" or "redshirt sophomore" => "So",
            "jr" or "junior" or "r-jr" or "redshirt junior" => "Jr",
            "sr" or "senior" or "r-sr" or "redshirt senior" => "Sr",
            "graduate" or "gs" => "GS",
            _ => raw,
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
