using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

var root = FindRepoRoot();
var dataDir = Path.Combine(root, "data");
var testDir = Path.Combine(root, "src/ASPAssistant.Tests/TestData");
Console.WriteLine($"data: {dataDir} exists={Directory.Exists(dataDir)}");

var operators = await new ASPAssistant.Core.Data.JsonDataStore(dataDir).LoadOperatorsAsync();
var (equipment, _) = await new ASPAssistant.Core.Data.JsonDataStore(dataDir).LoadEquipmentAsync();
var shopNames = operators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).Distinct().ToList();

var samples = new (string File, GameShopTheme Theme, string[] Expected)[]
{
    ("shop-tier2-color.png", GameShopTheme.Closure,
        ["聚合急救箱", "渡桥", "杰克", "陨星", "艾丝黛尔"]),
    ("shop-terminal-tier1-color.png", GameShopTheme.Terminal,
        ["虹彩活血饮料", "卡达", "慕斯", "角峰"]),
    ("shop-terminal-tier2-duplicates.png", GameShopTheme.Terminal,
        ["源石溶剂", "地灵", "陨星", "褐果", "地灵"]),
    ("shop-terminal-tier4-gray.png", GameShopTheme.Terminal,
        ["随身身份牌", "斯卡蒂", "深巡", "复奏", "风絮", "耶拉"]),
    ("shop-frontier-tier1-color.png", GameShopTheme.Ursus,
        ["坚守盾牌", "野鬃", "角峰", "褐果"]),
    ("shop-frontier-tier2-frozen-gray.png", GameShopTheme.Ursus,
        ["卫戍推荐信", "砾", "哈洛德", "雪猎", "云迹"]),
};

var ocr = new MaaOcrEngine(dataDir);
var reader = new ShopReader(ocr, shopNames);
var failures = 0;
foreach (var (file, theme, expected) in samples)
{
    var bytes = File.ReadAllBytes(Path.Combine(testDir, file));
    var (w, h) = ImageBytes.GetDimensions(bytes);
    reader.Theme = theme;
    var items = await reader.ReadAsync(bytes, w, h, shopNames);
    var got = items.Select(i => i.Name).ToList();
    var ok = expected.All(name => got.Contains(name));
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {file} ({w}x{h}) → {got.Count} 项: {string.Join(" / ", got)}");
}

// Tracking round-trip: add -> save -> reload, the path behind the in-app "追踪" buttons.
var tempSettings = Path.Combine(Path.GetTempPath(), "aspa-macprobe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempSettings);
var store = new ASPAssistant.Core.Data.JsonDataStore(dataDir);
var (covenants, jobChangeMap) = await store.LoadCovenantsAsync();
var calculator = new ASPAssistant.Core.Services.CovenantCalculator(operators, covenants, jobChangeMap);
var settingsManager = new ASPAssistant.Core.Data.SettingsManager(tempSettings);
var trackingVm = new ASPAssistant.Core.ViewModels.TrackingViewModel();
trackingVm.AddTracking("褐果", TrackingType.Operator);
trackingVm.AddTracking("随身身份牌", TrackingType.Equipment);
await settingsManager.SaveTrackingStateAsync(trackingVm.CaptureState());
var reloaded = await settingsManager.LoadTrackingStateAsync(calculator.EquipmentRules);
var roundTrip = reloaded.Entries.Any(e => e.Name == "褐果" && e.Type == TrackingType.Operator)
    && reloaded.Entries.Any(e => e.Name == "随身身份牌" && e.Type == TrackingType.Equipment)
    && !reloaded.Entries.Any(e => e.Name == "拜松");
Console.WriteLine(roundTrip ? "TRACKING ROUND-TRIP PASS" : "TRACKING ROUND-TRIP FAIL");
if (!roundTrip) failures++;

var detector = new GameShopThemeDetector(Path.Combine(dataDir, "shop_theme_refs.json"));
foreach (var file in new[] { "shop-terminal-tier1-color.png", "shop-frontier-tier1-color.png" })
{
    var bytes = File.ReadAllBytes(Path.Combine(testDir, file));
    Console.WriteLine($"theme {file} → {detector.Detect(bytes)}  scores: "
        + string.Join(", ", detector.Score(bytes).Select(x => $"{x.Theme}={x.Score:F1}")));
}

ocr.Dispose();
Console.WriteLine(failures == 0 ? "ALL SAMPLES PASS" : $"{failures} SAMPLE(S) FAILED");
return failures == 0 ? 0 : 1;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "data", "operators.json"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("repository root (data/operators.json) not found");
}
