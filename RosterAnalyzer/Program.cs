using Madden26Plugin.Roster;

var filePath = @"C:\Users\Ninja\source\repos\FMT.Madden26Plugin\ROSTER-Official.bin";
var reader = new CFB27RosterReader();
var data = reader.ReadRoster(File.ReadAllBytes(filePath));

// Find real-looking names with Heavy body type (largest players)
var heavy = data.Players.Where(p => p.BodyType == "Heavy_BodyType" && p.FirstName.Length > 2 && !p.FirstName.StartsWith("Np")).ToList();
Console.WriteLine($"=== HEAVY BODY TYPE (real names) — {heavy.Count} total ===\n");
foreach (var p in heavy.Take(20))
    Console.WriteLine($"{p.FirstName} {p.LastName} (stream {p.StreamIndex})");

Console.WriteLine($"\n=== MUSCULAR BODY TYPE (real names) — first 20 ===\n");
var muscular = data.Players.Where(p => p.BodyType == "Muscular_BodyType" && p.FirstName.Length > 2 && !p.FirstName.StartsWith("Np")).Take(20);
foreach (var p in muscular)
    Console.WriteLine($"{p.FirstName} {p.LastName} (stream {p.StreamIndex})");

Console.WriteLine($"\n=== THIN BODY TYPE (real names) — first 10 ===\n");
var thin = data.Players.Where(p => p.BodyType == "Thin_BodyType" && p.FirstName.Length > 2 && !p.FirstName.StartsWith("Np")).Take(10);
foreach (var p in thin)
    Console.WriteLine($"{p.FirstName} {p.LastName} (stream {p.StreamIndex})");
