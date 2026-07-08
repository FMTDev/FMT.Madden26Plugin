using System.Text.RegularExpressions;

namespace Madden26Plugin.Roster;

public static class On3TeamMap
{
    private static readonly Dictionary<string, string> ExplicitSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "alabama-crimson-tide",
        ["Appalachian State"] = "appalachian-state-mountaineers",
        ["Arizona"] = "arizona-wildcats",
        ["Arizona State"] = "arizona-state-sun-devils",
        ["Arkansas"] = "arkansas-razorbacks",
        ["Arkansas State"] = "arkansas-state-red-wolves",
        ["Army"] = "army-black-knights",
        ["Auburn"] = "auburn-tigers",
        ["Ball State"] = "ball-state-cardinals",
        ["Baylor"] = "baylor-bears",
        ["Boise State"] = "boise-state-broncos",
        ["Boston College"] = "boston-college-eagles",
        ["Bowling Green"] = "bowling-green-falcons",
        ["Buffalo"] = "buffalo-bulls",
        ["BYU"] = "byu-cougars",
        ["California"] = "california-golden-bears",
        ["Central Michigan"] = "central-michigan-chippewas",
        ["Charlotte"] = "charlotte-49ers",
        ["Cincinnati"] = "cincinnati-bearcats",
        ["Clemson"] = "clemson-tigers",
        ["Coastal Carolina"] = "coastal-carolina-chanticleers",
        ["Colorado"] = "colorado-buffaloes",
        ["Colorado State"] = "colorado-state-rams",
        ["Connecticut"] = "uconn-huskies",
        ["Duke"] = "duke-blue-devils",
        ["East Carolina"] = "east-carolina-pirates",
        ["Eastern Michigan"] = "eastern-michigan-eagles",
        ["Florida"] = "florida-gators",
        ["Florida Atlantic"] = "florida-atlantic-owls",
        ["Florida International"] = "fiu-panthers",
        ["Florida State"] = "florida-state-seminoles",
        ["Fresno State"] = "fresno-state-bulldogs",
        ["Georgia"] = "georgia-bulldogs",
        ["Georgia Southern"] = "georgia-southern-eagles",
        ["Georgia State"] = "georgia-state-panthers",
        ["Georgia Tech"] = "georgia-tech-yellow-jackets",
        ["Hawaii"] = "hawaii-rainbow-warriors",
        ["Houston"] = "houston-cougars",
        ["Illinois"] = "illinois-fighting-illini",
        ["Indiana"] = "indiana-hoosiers",
        ["Iowa"] = "iowa-hawkeyes",
        ["Iowa State"] = "iowa-state-cyclones",
        ["Jacksonville State"] = "jacksonville-state-gamecocks",
        ["James Madison"] = "james-madison-dukes",
        ["Kansas"] = "kansas-jayhawks",
        ["Kansas State"] = "kansas-state-wildcats",
        ["Kennesaw State"] = "kennesaw-state-owls",
        ["Kent State"] = "kent-state-golden-flashes",
        ["Kentucky"] = "kentucky-wildcats",
        ["Liberty"] = "liberty-flames",
        ["Louisiana"] = "louisiana-ragin-cajuns",
        ["Louisiana-Monroe"] = "ul-monroe-warhawks",
        ["Louisiana Tech"] = "louisiana-tech-bulldogs",
        ["Louisville"] = "louisville-cardinals",
        ["LSU"] = "lsu-tigers",
        ["Marshall"] = "marshall-thundering-herd",
        ["Maryland"] = "maryland-terrapins",
        ["Memphis"] = "memphis-tigers",
        ["Miami"] = "miami-hurricanes",
        ["Miami (OH)"] = "miami-redhawks",
        ["Michigan"] = "michigan-wolverines",
        ["Michigan State"] = "michigan-state-spartans",
        ["Middle Tennessee"] = "middle-tennessee-blue-raiders",
        ["Minnesota"] = "minnesota-golden-gophers",
        ["Mississippi State"] = "mississippi-state-bulldogs",
        ["Missouri"] = "missouri-tigers",
        ["Navy"] = "navy-midshipmen",
        ["NC State"] = "nc-state-wolfpack",
        ["Nebraska"] = "nebraska-cornhuskers",
        ["Nevada"] = "nevada-wolf-pack",
        ["New Mexico"] = "new-mexico-lobos",
        ["New Mexico State"] = "new-mexico-state-aggies",
        ["North Carolina"] = "north-carolina-tar-heels",
        ["North Texas"] = "north-texas-mean-green",
        ["Northern Illinois"] = "northern-illinois-huskies",
        ["Northwestern"] = "northwestern-wildcats",
        ["Notre Dame"] = "notre-dame-fighting-irish",
        ["Ohio"] = "ohio-bobcats",
        ["Ohio State"] = "ohio-state-buckeyes",
        ["Oklahoma"] = "oklahoma-sooners",
        ["Oklahoma State"] = "oklahoma-state-cowboys",
        ["Old Dominion"] = "old-dominion-monarchs",
        ["Ole Miss"] = "ole-miss-rebels",
        ["Oregon"] = "oregon-ducks",
        ["Oregon State"] = "oregon-state-beavers",
        ["Penn State"] = "penn-state-nittany-lions",
        ["Pittsburgh"] = "pittsburgh-panthers",
        ["Purdue"] = "purdue-boilermakers",
        ["Rice"] = "rice-owls",
        ["Rutgers"] = "rutgers-scarlet-knights",
        ["Sam Houston"] = "sam-houston-bearkats",
        ["San Diego State"] = "san-diego-state-aztecs",
        ["San José State"] = "san-jose-state-spartans",
        ["SMU"] = "smu-mustangs",
        ["South Alabama"] = "south-alabama-jaguars",
        ["South Carolina"] = "south-carolina-gamecocks",
        ["South Florida"] = "south-florida-bulls",
        ["Southern Miss"] = "southern-miss-golden-eagles",
        ["Stanford"] = "stanford-cardinal",
        ["Syracuse"] = "syracuse-orange",
        ["TCU"] = "tcu-horned-frogs",
        ["Temple"] = "temple-owls",
        ["Tennessee"] = "tennessee-volunteers",
        ["Texas"] = "texas-longhorns",
        ["Texas A&M"] = "texas-am-aggies",
        ["Texas State"] = "texas-state-bobcats",
        ["Texas Tech"] = "texas-tech-red-raiders",
        ["Toledo"] = "toledo-rockets",
        ["Troy"] = "troy-trojans",
        ["Tulane"] = "tulane-green-wave",
        ["Tulsa"] = "tulsa-golden-hurricane",
        ["UAB"] = "uab-blazers",
        ["UCF"] = "ucf-knights",
        ["UCLA"] = "ucla-bruins",
        ["UMass"] = "umass-minutemen",
        ["UNLV"] = "unlv-rebels",
        ["USC"] = "usc-trojans",
        ["UTEP"] = "utep-miners",
        ["UTSA"] = "utsa-roadrunners",
        ["Utah"] = "utah-utes",
        ["Utah State"] = "utah-state-aggies",
        ["Vanderbilt"] = "vanderbilt-commodores",
        ["Virginia"] = "virginia-cavaliers",
        ["Virginia Tech"] = "virginia-tech-hokies",
        ["Wake Forest"] = "wake-forest-demon-deacons",
        ["Washington"] = "washington-huskies",
        ["Washington State"] = "washington-state-cougars",
        ["West Virginia"] = "west-virginia-mountaineers",
        ["Western Kentucky"] = "western-kentucky-hilltoppers",
        ["Western Michigan"] = "western-michigan-broncos",
        ["Wisconsin"] = "wisconsin-badgers",
        ["Wyoming"] = "wyoming-cowboys",
        ["Air Force"] = "air-force-falcons",
        ["Akron"] = "akron-zips",
        ["FIU"] = "fiu-panthers",
    };

    public static bool TryGetSlug(string teamName, out string slug)
    {
        if (ExplicitSlugs.TryGetValue(teamName, out slug))
            return true;

        slug = DeriveSlug(teamName);
        return !string.IsNullOrEmpty(slug);
    }

    private static string DeriveSlug(string teamName)
    {
        if (!SidearmTeamMap.Teams.TryGetValue(teamName, out var domain))
            return SlugifyName(teamName);

        var slug = SlugifyName(teamName);
        var mascot = ExtractMascotFromDomain(domain);
        if (!string.IsNullOrEmpty(mascot))
            slug += "-" + mascot;

        return slug;
    }

    private static string SlugifyName(string name)
    {
        var s = name.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"-+", "-");
        s = s.Trim('-');
        return s;
    }

    private static readonly string[] KnownMascotEndings =
    {
        "fightingillini", "goldengophers", "nittanylions", "thunderingherd",
        "goldenflashes", "mean green", "demondeacons", "goldenhurricane",
        "hornedfrogs", "crimsontide", "ragincajuns", "goldenbears",
        "blackknights", "bluedevils", "sun devils", "yellowjackets",
        "greenwave", "gamecocks", "cavaliers", "midshipmen",
        "cornhuskers", "fightingirish", "minutemen", "redhawks",
        "roadrunners", "terrapins", "mustangs", "wildcats",
        "broncos", "bulls", "cardinal", "cardinals",
        "cyclones", "eagles", "falcons", "gators",
        "hawkeyes", "hoosiers", "huskies", "huskers",
        "jaguars", "longhorns", "mountaineers", "owls",
        "panthers", "pirates", "raiders", "rams",
        "rebels", "rockets", "sooners", "spartans",
        "tigers", "trojans", "utes", "volunteers",
        "wolfpack", "wolverines", "badgers", "bears",
        "beavers", "bobcats", "bruins", "buckeyes",
        "buffaloes", "bulldogs", "chanticleers", "chippewas",
        "cougars", "cowboys", "ducks", "flames",
        "frogs", "gophers", "hawks", "hilltoppers",
        "hokies", "hurricanes", "jayhawks", "knights",
        "lions", "lobos", "miners", "monarchs",
        "orange", "paladins", "red wolves", "seminoles",
        "tar heels", "terriers", "warhawks", "wolf pack",
        "aggies", "aztecs", "blazers", "blue raiders",
        "boilermakers", "broncs", "buckeyes", "bearcats",
        "49ers", "bearkats", "cajuns", "commodores",
    };

    private static string ExtractMascotFromDomain(string domain)
    {
        domain = domain.ToLowerInvariant();
        foreach (var tld in new[] { ".com", ".net", ".org", ".edu" })
            if (domain.EndsWith(tld)) domain = domain[..^tld.Length];
        foreach (var p in new[] { "go", "the", "got", "g" })
            if (domain.StartsWith(p)) domain = domain[p.Length..];
        foreach (var s in new[] { "sports", "athletics" })
            if (domain.EndsWith(s)) domain = domain[..^s.Length];
        if (domain.EndsWith("online")) domain = domain[..^"online".Length];

        domain = Regex.Replace(domain, @"\d+", "");

        var bestMascot = "";
        foreach (var mascot in KnownMascotEndings.OrderByDescending(m => m.Length))
        {
            if (domain.EndsWith(mascot.Replace(" ", "")))
            {
                bestMascot = mascot;
                break;
            }
        }

        return bestMascot;
    }

    public static string GetSlugOrDefault(string teamName)
    {
        return TryGetSlug(teamName, out var slug) ? slug : SlugifyName(teamName);
    }
}
