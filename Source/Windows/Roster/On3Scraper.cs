using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Madden26Plugin.Roster;

public class On3Scraper : IDisposable
{
    private readonly HttpClient _httpClient;

    public On3Scraper()
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
        var url = $"https://www.on3.com/college/{teamSlug}/football/{year}/roster/";
        return await FetchRosterFromUrlAsync(url);
    }

    public async Task<List<NcaaPlayerInfo>> FetchRosterFromUrlAsync(string url)
    {
        var html = await FetchPageAsync(url);
        return ParseRosterHtml(html);
    }

    private async Task<string> FetchPageAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public static List<NcaaPlayerInfo> ParseRosterHtml(string html)
    {
        var players = new List<NcaaPlayerInfo>();

        var nextDataMatch = Regex.Match(html,
            @"<script\s+id=""__NEXT_DATA__""[^>]*type=""application\/json""[^>]*>(.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!nextDataMatch.Success)
            return players;

        var json = nextDataMatch.Groups[1].Value;

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("props", out var props) ||
            !props.TryGetProperty("pageProps", out var pageProps) ||
            !pageProps.TryGetProperty("rosterList", out var rosterList) ||
            !rosterList.TryGetProperty("list", out var roster))
            return players;

        var seen = new HashSet<string>();

        foreach (var entry in roster.EnumerateArray())
        {
            if (!entry.TryGetProperty("player", out var playerJson))
                continue;

            var name = playerJson.TryGetProperty("fullName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String
                ? fnProp.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;

            int? jerseyNumber = null;
            if (playerJson.TryGetProperty("jerseyNumber", out var jn))
            {
                if (jn.ValueKind == JsonValueKind.Number)
                    jerseyNumber = jn.GetInt32();
                else if (jn.ValueKind == JsonValueKind.String && int.TryParse(jn.GetString(), out var jnParsed))
                    jerseyNumber = jnParsed;
            }

            string position = null;
            if (playerJson.TryGetProperty("position", out var posProp) && posProp.ValueKind == JsonValueKind.Object)
                position = posProp.TryGetProperty("abbr", out var abbr) && abbr.ValueKind == JsonValueKind.String
                    ? abbr.GetString() : null;

            string heightStr = null;
            if (playerJson.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.String)
                heightStr = hProp.GetString();

            int? weight = null;
            if (playerJson.TryGetProperty("weight", out var wProp) && wProp.ValueKind == JsonValueKind.Number)
                weight = wProp.GetInt32();

            string classRank = null;
            if (playerJson.TryGetProperty("classRank", out var crProp) && crProp.ValueKind == JsonValueKind.String)
                classRank = crProp.GetString();

            var key = $"{jerseyNumber}_{name}";
            if (!seen.Add(key)) continue;

            players.Add(new NcaaPlayerInfo
            {
                Name = name,
                Position = NormalizePosition(position),
                ClassYear = NormalizeClassYear(classRank),
                JerseyNumber = jerseyNumber,
                Weight = weight,
                HeightInches = ParseHeight(heightStr),
            });
        }

        return players;
    }

    internal static string NormalizePosition(string pos)
    {
        if (string.IsNullOrEmpty(pos)) return null;
        return pos.Trim().ToUpperInvariant() switch
        {
            "IOL" => "OL",
            "EDGE" => "DE",
            _ => pos.Trim().ToUpperInvariant(),
        };
    }

    internal static double? ParseHeight(string heightStr)
    {
        if (string.IsNullOrEmpty(heightStr)) return null;
        var m = Regex.Match(heightStr, @"(\d+)[-.](\d+(?:\.\d+)?)");
        if (m.Success)
        {
            var feet = int.Parse(m.Groups[1].Value);
            var inches = double.Parse(m.Groups[2].Value);
            return feet * 12 + inches;
        }
        var simple = Regex.Match(heightStr, @"(\d+)[-.](\d+)");
        if (simple.Success)
            return int.Parse(simple.Groups[1].Value) * 12 + int.Parse(simple.Groups[2].Value);
        return null;
    }

    internal static string NormalizeClassYear(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        // On3 __NEXT_DATA__ uses values like: Freshman, Sophomore, Junior, Senior,
        // RedShirt Freshman, RedShirt Sophomore, RedShirt Junior, RedShirt Senior, Graduate
        return raw.Trim() switch
        {
            "Freshman" => "Fr",
            "Sophomore" => "So",
            "Junior" => "Jr",
            "Senior" => "Sr",
            "RedShirt Freshman" => "RFr",
            "RedShirt Sophomore" => "RSo",
            "RedShirt Junior" => "RJr",
            "RedShirt Senior" => "RSr",
            "Graduate" => "GS",
            _ => raw,
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
