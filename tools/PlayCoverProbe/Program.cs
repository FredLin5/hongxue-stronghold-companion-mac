// Live probe: capture one frame from the running PlayCover game through MaaTools and
// optionally run the shop reader, so the real connection can be checked without the UI.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

var root = FindRepoRoot();
var outPath = args.Length > 0 ? args[0] : "/tmp/live-frame.png";
using var connection = new GameConnectionService(new GameConnectionSettings
{
    Source = GameCaptureSource.PlayCover,
    PlayCoverAddress = Environment.GetEnvironmentVariable("PLAYCOVER_ADDRESS") ?? "127.0.0.1:1717",
    PlayCoverBundleId = Environment.GetEnvironmentVariable("PLAYCOVER_BUNDLE") ?? "com.hypergryph.arknights",
});

var watch = args.Contains("--watch");
var frame = connection.CaptureDeviceScreen(1920);
Console.WriteLine($"status: {connection.Status}");
if (frame is null)
{
    Console.WriteLine("capture failed");
    return 1;
}

var (width, height) = ImageBytes.GetDimensions(frame);
using var decoded = Raster.Decode(frame);
File.WriteAllBytes(outPath, decoded?.EncodePng() ?? frame);
Console.WriteLine($"captured {width}x{height} -> {outPath}");

var dataDir = Path.Combine(root, "data");
var detector = new GameShopThemeDetector(Path.Combine(dataDir, "shop_theme_refs.json"));
Console.WriteLine($"theme(1920, threshold 30): {detector.Detect(frame, 30)}");
Console.WriteLine("scores: " + string.Join(", ", detector.Score(frame).Select(s => $"{s.Theme}={s.Score:F1}")));
var full = connection.CaptureDeviceScreen(4000);
if (full is not null)
{
    var (fw, fh) = ImageBytes.GetDimensions(full);
    Console.WriteLine($"full {fw}x{fh} theme: {detector.Detect(full)}");
    using var fullRaster = Raster.Decode(full);
    File.WriteAllBytes(outPath.Replace(".png", "-full.png"), fullRaster?.EncodePng() ?? full);
}

if (watch)
{
    // Keep sampling until the shop theme is recognised (the user opens the shop meanwhile).
    for (int attempt = 1; attempt <= 120; attempt++)
    {
        Thread.Sleep(2000);
        var next = connection.CaptureDeviceScreen(1920);
        if (next is null) { Console.WriteLine($"[{attempt}] capture failed: {connection.Status}"); continue; }
        var theme = detector.Detect(next);
        if (theme is null) { Console.WriteLine($"[{attempt}] no shop theme: {connection.Status}"); continue; }
        frame = next;
        Console.WriteLine($"[{attempt}] shop theme detected: {theme}");
        break;
    }
    var (w2, h2) = ImageBytes.GetDimensions(frame);
    width = w2; height = h2;
    using var shot = Raster.Decode(frame);
    File.WriteAllBytes(outPath, shot?.EncodePng() ?? frame);
    Console.WriteLine($"saved {outPath} {width}x{height}");
}

var store = new ASPAssistant.Core.Data.JsonDataStore(dataDir);
var operators = await store.LoadOperatorsAsync();
var (equipment, _) = await store.LoadEquipmentAsync();
var shopNames = operators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).Distinct().ToList();
using var ocr = new MaaOcrEngine(dataDir);
var reader = new ShopReader(ocr, shopNames) { Theme = detector.Detect(frame) ?? GameShopTheme.Terminal };
var items = await reader.ReadAsync(frame, width, height, shopNames);
Console.WriteLine($"shop items ({items.Count}): {string.Join(" / ", items.Select(i => i.Name))}");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "data", "operators.json"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("repository root not found");
}
