namespace Madden26Plugin.Roster;

public class FaceTemplateMatcher
{
    private record TemplateEntry(string GenericId, string BodyType, SkinToneGroup SkinTone, int HairColorIndex = 0);

    private readonly List<TemplateEntry> _templates;
    private readonly Dictionary<string, int> _assignmentCount = new();
    private readonly Random _rng = new();

    /// <summary>Maps position→actual body types from the loaded roster data.</summary>
    private readonly Dictionary<string, HashSet<string>> _positionBodyTypes = new(StringComparer.OrdinalIgnoreCase);

    public FaceTemplateMatcher(IEnumerable<PlayerVisualRecipe> players)
    {
        var list = players.ToList();

        // Build position→body type mapping from actual roster data
        foreach (var p in list.Where(p => p.IsGenericPlayer
            && !string.IsNullOrEmpty(p.Position) && !string.IsNullOrEmpty(p.BodyType)))
        {
            if (!_positionBodyTypes.ContainsKey(p.Position))
                _positionBodyTypes[p.Position] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _positionBodyTypes[p.Position].Add(p.BodyType);
        }

        var withBodyType = list
            .Where(p => p.IsGenericPlayer && !string.IsNullOrEmpty(p.BodyType))
            .Select(p => new TemplateEntry(
                p.UniqueId,
                p.BodyType,
                p.SkinTone != SkinToneGroup.Unknown
                    ? p.SkinTone
                    : ComplexionPresetMapper.MapToneValue(
                        ComplexionPresetMapper.ExtractSkinToneBaseValue(p.UniqueId)),
                HairColorMapper.ExtractHairColorIndex(p.UniqueId)))
            .Distinct()
            .ToList();

        if (withBodyType.Count > 0)
        {
            _templates = withBodyType;
        }
        else
        {
            _templates = list
                .Where(p => p.IsGenericPlayer && !string.IsNullOrEmpty(p.UniqueId))
                .Select(p => new TemplateEntry(
                    p.UniqueId,
                    "",
                    p.SkinTone != SkinToneGroup.Unknown
                        ? p.SkinTone
                        : ComplexionPresetMapper.MapToneValue(
                            ComplexionPresetMapper.ExtractSkinToneBaseValue(p.UniqueId)),
                HairColorMapper.ExtractHairColorIndex(p.UniqueId)))
                .Distinct()
                .ToList();
        }
    }

    public FaceTemplateMatcher(IEnumerable<PlayerVisualRecipe> players, ComplexionPresetMapper complexionMapper)
        : this(players)
    {
        var mapped = complexionMapper.GenericToGroup;
        for (var i = 0; i < _templates.Count; i++)
        {
            var t = _templates[i];
            if (mapped.TryGetValue(t.GenericId, out var group) && group != SkinToneGroup.Unknown)
                _templates[i] = t with { SkinTone = group };
        }
    }

    public int AvailableTemplateCount => _templates.Count;

    public int DistinctBodyTypeCount
        => _templates.Select(t => t.BodyType).Distinct().Count();

    public string PickTemplate(string position)
    {
        if (_templates.Count == 0) return "";
        var candidates = GetCandidates(position);
        return PickLeastUsed(candidates);
    }

    public string PickTemplate(string position, SkinToneGroup preferredTone)
    {
        if (_templates.Count == 0) return "";
        var candidates = GetCandidates(position);

        if (preferredTone != SkinToneGroup.Unknown)
        {
            var sameTone = candidates
                .Where(t => t.SkinTone == preferredTone)
                .ToList();
            if (sameTone.Count > 0)
                candidates = sameTone;
        }

        return PickLeastUsed(candidates);
    }

    private List<TemplateEntry> GetCandidates(string position)
    {
        // Use actual body types from roster data for this position
        if (!string.IsNullOrEmpty(position) && _positionBodyTypes.TryGetValue(position, out var bodyTypes) && bodyTypes.Count > 0)
        {
            var matched = _templates
                .Where(t => bodyTypes.Contains(t.BodyType))
                .ToList();
            if (matched.Count > 0)
                return matched;
        }

        // Fallback: all templates
        return _templates.ToList();
    }

    public string MapPositionToBodyType(string position)
    {
        return "QB_BodyType";
    }

    private string PickLeastUsed(List<TemplateEntry> candidates)
    {
        var minCount = candidates.Min(c => _assignmentCount.TryGetValue(c.GenericId, out var count) ? count : 0);
        var pool = candidates.Where(c => (_assignmentCount.TryGetValue(c.GenericId, out var count) ? count : 0) == minCount).ToList();
        var chosen = pool[_rng.Next(pool.Count)];

        _assignmentCount.TryGetValue(chosen.GenericId, out var current);
        _assignmentCount[chosen.GenericId] = current + 1;

        return chosen.GenericId;
    }

    public void AssignSkinTone(PlayerVisualRecipe player, SkinToneGroup tone)
    {
        player.SkinTone = tone;
        var idx = _templates.FindIndex(t =>
            string.Equals(t.GenericId, player.UniqueId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            _templates[idx] = _templates[idx] with { SkinTone = tone };
    }

    public SkinToneGroup GetSkinTone(string genericId)
    {
        var idx = _templates.FindIndex(t =>
            string.Equals(t.GenericId, genericId, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? _templates[idx].SkinTone : SkinToneGroup.Unknown;
    }

    public int GetHairColorIndex(string genericId)
    {
        if (string.IsNullOrEmpty(genericId)) return 0;
        var idx = _templates.FindIndex(t =>
            string.Equals(t.GenericId, genericId, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? _templates[idx].HairColorIndex : HairColorMapper.ExtractHairColorIndex(genericId);
    }

    public string GetHairColorDescription(string genericId)
    {
        var hcIndex = GetHairColorIndex(genericId);
        return HairColorMapper.GetHairColorDescription(hcIndex);
    }

    public Dictionary<string, int> GetAssignmentStats()
        => new(_assignmentCount);

    public List<string> GetAvailableFaces(string position)
    {
        var candidates = GetCandidates(position);
        var faces = candidates
            .Select(t => t.GenericId)
            .Distinct()
            .OrderBy(f => f)
            .ToList();
        return faces;
    }
}
