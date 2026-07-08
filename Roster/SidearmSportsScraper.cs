using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Madden26Plugin.Roster;

public class SidearmSportsScraper : IDisposable
{
    private readonly HttpClient _httpClient;

    public SidearmSportsScraper()
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

    public async Task<List<NcaaPlayerInfo>> FetchTeamRosterAsync(string domain, int year)
    {
        var url = $"https://{domain}/sports/football/roster/{year}";
        var html = await FetchPageAsync(url);
        return ParseRosterHtml(html, domain);
    }

    public async Task<List<NcaaPlayerInfo>> FetchTeamRosterFromUrlAsync(string url)
    {
        var html = await FetchPageAsync(url);
        var domain = new Uri(url).Host;
        return ParseRosterHtml(html, domain);
    }

    private async Task<string> FetchPageAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public static List<NcaaPlayerInfo> ParseRosterHtml(string html, string teamSlug)
    {
        var players = new List<NcaaPlayerInfo>();

        var cardMatches = Regex.Matches(html,
            @"aria-label=""([^""]+?)\s+jersey\s+number\s+(\d+)\s+full\s+bio""",
            RegexOptions.IgnoreCase);

        foreach (Match match in cardMatches)
        {
            var player = new NcaaPlayerInfo { Team = teamSlug };
            player.Name = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (int.TryParse(match.Groups[2].Value, out var jersey))
                player.JerseyNumber = jersey;

            var posStart = match.Index + match.Length;
            var searchEnd = Math.Min(posStart + 2000, html.Length);
            if (posStart < html.Length)
            {
                var context = html.Substring(posStart, searchEnd - posStart);

                var hasPosition = !string.IsNullOrEmpty(ExtractBioStat(context, "Position"));
                if (!hasPosition)
                    continue;

                player.Position = ExtractBioStat(context, "Position");
                var classYear = ExtractBioStat(context, "Academic Year");
                player.ClassYear = NormalizeClassYear(classYear);

                var heightStr = ExtractBioStat(context, "Height");
                if (!string.IsNullOrEmpty(heightStr))
                    player.HeightInches = ParseHeight(heightStr);

                var weightStr = ExtractBioStat(context, "Weight");
                if (!string.IsNullOrEmpty(weightStr) &&
                    int.TryParse(Regex.Match(weightStr, @"\d+").Value, out var weight))
                    player.Weight = weight;
            }

            if (!string.IsNullOrEmpty(player.Name))
                players.Add(player);
        }

        return players;
    }

    private static string ExtractBioStat(string context, string label)
    {
        var pattern = $@"<span\s+class=""sr-only""[^>]*>\s*{Regex.Escape(label)}\s*</span>\s*([^<]+)";
        var match = Regex.Match(context, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
            return WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
        return null;
    }

    internal static double? ParseHeight(string heightStr)
    {
        heightStr = WebUtility.HtmlDecode(heightStr);
        var match = Regex.Match(heightStr, @"(\d+)\s*['\u2032]\s*(\d+)?\s*['\u2033\u2032]?");
        if (match.Success)
        {
            var feet = int.Parse(match.Groups[1].Value);
            var inches = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            return feet * 12 + inches;
        }
        return null;
    }

    internal static string NormalizeClassYear(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return raw.TrimEnd('.').ToLowerInvariant() switch
        {
            "fr" or "freshman" or "rfreshman" or "rfr" => "Fr",
            "so" or "sophomore" or "rsophomore" or "rso" => "So",
            "jr" or "junior" or "rjunior" or "rjr" => "Jr",
            "sr" or "senior" or "rsenior" or "rsr" => "Sr",
            "gs" or "graduate" or "grad" => "GS",
            string s when s.EndsWith("freshman") || s.EndsWith("fr") => "Fr",
            string s when s.EndsWith("sophomore") || s.EndsWith("so") => "So",
            string s when s.EndsWith("junior") || s.EndsWith("jr") => "Jr",
            string s when s.EndsWith("senior") || s.EndsWith("sr") => "Sr",
            _ => raw,
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
