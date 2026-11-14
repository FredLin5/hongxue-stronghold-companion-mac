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
var tapIndex = Array.IndexOf(args, "--tap");
var swipeIndex = Array.IndexOf(args, "--swipe");
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

// Optional interaction: coordinates are given in a 1920x1080 reference frame and scaled
// to the real capture size (the game renders 3840x2160 through PlayCover here).
if (tapIndex >= 0 && args.Length > tapIndex + 2)
{
    var (cw, ch) = connection.DeviceSize;
    double sx = cw / 1920.0, sy = ch / 1080.0;
    int x = (int)(double.Parse(args[tapIndex + 1]) * sx);
    int y = (int)(double.Parse(args[tapIndex + 2]) * sy);
    Console.WriteLine($"tap ({args[tapIndex + 1]},{args[tapIndex + 2]}) -> ({x},{y}) of {cw}x{ch}: {connection.Tap(x, y)}");
    Thread.Sleep(1200);
    var after = connection.CaptureDeviceScreen(1920);
    if (after is not null)
    {
        using var shot = Raster.Decode(after);
        File.WriteAllBytes(outPath.Replace(".png", "-after-tap.png"), shot?.EncodePng() ?? after);
        Console.WriteLine("saved after-tap frame");
    }
    return 0;
}

if (swipeIndex >= 0 && args.Length > swipeIndex + 4)
{
    var (cw, ch) = connection.DeviceSize;
    double sx = cw / 1920.0, sy = ch / 1080.0;
    int x1 = (int)(double.Parse(args[swipeIndex + 1]) * sx);
    int y1 = (int)(double.Parse(args[swipeIndex + 2]) * sy);
    int x2 = (int)(double.Parse(args[swipeIndex + 3]) * sx);
    int y2 = (int)(double.Parse(args[swipeIndex + 4]) * sy);
    Console.WriteLine($"swipe ({x1},{y1})->({x2},{y2}): {connection.Swipe(x1, y1, x2, y2)}");
    Thread.Sleep(800);
    var after = connection.CaptureDeviceScreen(1920);
    if (after is not null)
    {
        using var shot = Raster.Decode(after);
        File.WriteAllBytes(outPath.Replace(".png", "-after-swipe.png"), shot?.EncodePng() ?? after);
        Console.WriteLine("saved after-swipe frame");
    }
    return 0;
}

var dataDir = Path.Combine(root, "data");

var banFileIndex = Array.IndexOf(args, "--ban-file");
if (banFileIndex >= 0 && args.Length > banFileIndex + 1)
{
    frame = File.ReadAllBytes(args[banFileIndex + 1]);
}

if (args.Contains("--ban") || banFileIndex >= 0)
{
    var store2 = new ASPAssistant.Core.Data.JsonDataStore(dataDir);
    var ops = await store2.LoadOperatorsAsync();
    var (covs, _) = await store2.LoadCovenantsAsync();
    var coreNames = covs.Where(c => c.BondType.Equals("SEASON", StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Name).Where(n => n.Length > 0).ToList();
    var additionalNames = covs.Where(c => !c.BondType.Equals("SEASON", StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Name).Where(n => n.Length > 0).ToList();
    var bosses = new BossDataService();
    await bosses.LoadAsync(dataDir);
    using var ocr2 = new MaaOcrEngine(dataDir);
    var mode = new ASPAssistant.Core.GameModes.GarrisonProtocol.GarrisonProtocolMode();
    var recognizer = new EnemyRecognizer(ocr2, mode.OcrStrategy, bosses);
    var analyzer = new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(frame), ocr2, mode.OcrStrategy,
        recognizer, ops, coreNames, additionalNames);
    analyzer.AnalysisCompleted += r => Console.WriteLine(
        "bans core=" + string.Join(",", r.CoreCovenants.Where(c => c.IsBanned).Select(c => c.Name))
        + " | additional=" + string.Join(",", r.AdditionalCovenants.Where(c => c.IsBanned).Select(c => c.Name))
        + " | enemy=" + (r.EnemyFaction ?? "-"));
    analyzer.AutoBanListComputed += names => Console.WriteLine("auto bans: " + string.Join(" / ", names));
    analyzer.EnemyDetected += e => Console.WriteLine(
        $"enemy: boss={e.BossName} types={string.Join("/", e.EnemyTypes)}");
    analyzer.AnalysisFailed += m => Console.WriteLine("analysis failed: " + m);
    await analyzer.RunAnalysisAsync();
    return 0;
}
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
