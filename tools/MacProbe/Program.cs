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

// Banned covenant chips must be re-ordered to the end of the row.
var browseVm = new ASPAssistant.Core.ViewModels.OperatorBrowseViewModel();
browseVm.LoadOperators(operators.Where(o => o.RequiredStrategyId.Length == 0).ToList());
browseVm.SetCovenantBanChecker(n => n == "莱塔尼亚");
browseVm.NotifyBansChanged();
var blocked = browseVm.CoreChips.Select(c => c.IsBlocked).ToList();
var firstBlocked = blocked.IndexOf(true);
var sorted = firstBlocked < 0 || !blocked.Skip(firstBlocked).Any(x => !x);
Console.WriteLine(sorted ? "CHIP SORT PASS" : "CHIP SORT FAIL: " + string.Join(",", browseVm.CoreChips.Select(c => $"{c.Value}:{c.IsBlocked}")));
if (!roundTrip) failures++;

// ── 阵容规划：目标 → 搜索 → 保存 / 追踪（和 macOS「规划」页同一套 Core 代码）──
var plannerBans = new ASPAssistant.Core.ViewModels.BanViewModel();
var planner = new LineupPlanner(operators, covenants);
var plannerVm = new ASPAssistant.Core.ViewModels.LineupPlannerViewModel(
    planner, operators, plannerBans, calculator.EquipmentRules)
{
    DebounceMilliseconds = 0,
    IsActive = true,
    TrackedOperatorsProvider = () => [],
};
Lineup? savedLineup = null;
Lineup? trackedLineup = null;
plannerVm.SaveRequested += lineup => savedLineup = lineup;
plannerVm.TrackRequested += lineup => trackedLineup = lineup;
var goalCore = plannerVm.SelectGoal("乌萨斯", 1);
var goalExtra = plannerVm.SelectGoal("精准", 1);
await (plannerVm.PendingSearch ?? Task.CompletedTask);
var solvedVariants = plannerVm.VariantCells.Count(c => c.HasResults);
var goalsMet = plannerVm.VisibleLineups.All(item =>
    item.Source.Goals.All(g => item.Source.Counts.GetValueOrDefault(g.Name) >= g.Count));
var searchOk = goalCore && goalExtra && solvedVariants > 0 && goalsMet;
Console.WriteLine($"{(searchOk ? "PLANNER SEARCH PASS" : "PLANNER SEARCH FAIL")} · 目标 "
    + string.Join("、", plannerVm.GoalChips)
    + $" · {solvedVariants}/{plannerVm.VariantCells.Count} 组有解 · 当前 {plannerVm.VisibleLineups.Count} 套候选");
if (!searchOk) failures++;

// 暴躁铁皮：多一档转职（8 人 / 9 人 × 0~3 转 = 8 组）
plannerVm.SelectStrategyCommand.Execute(plannerVm.StrategyChoices.First(c => c.Strategy == PlannerStrategy.Mech));
await (plannerVm.PendingSearch ?? Task.CompletedTask);
var mechOk = plannerVm.TransformHeaders.Count == 4 && plannerVm.VariantCells.Count == 8;
Console.WriteLine($"{(mechOk ? "PLANNER STRATEGY PASS" : "PLANNER STRATEGY FAIL")} · 暴躁铁皮 "
    + $"{plannerVm.VariantCells.Count} 组 / {plannerVm.TransformHeaders.Count} 档转职");
if (!mechOk) failures++;
plannerVm.SelectStrategyCommand.Execute(plannerVm.StrategyChoices.First(c => c.Strategy == PlannerStrategy.Other));
await (plannerVm.PendingSearch ?? Task.CompletedTask);

var firstPlan = plannerVm.VisibleLineups.FirstOrDefault();
if (firstPlan is not null)
{
    plannerVm.SaveLineupCommand.Execute(firstPlan);
    plannerVm.TrackLineupCommand.Execute(firstPlan);
}
var plannerStore = new ASPAssistant.Core.Data.LineupStore(
    Path.Combine(tempSettings, "lineups"), calculator.EquipmentRules);
if (savedLineup is not null) await plannerStore.SaveAsync(savedLineup);
var plannedSaved = savedLineup is null ? [] : await plannerStore.LoadAllAsync();
var saveOk = savedLineup is not null
    && plannedSaved.Any(l => l.Id == savedLineup.Id && l.Slots.Count == savedLineup.Slots.Count && l.Slots.Count > 0);
Console.WriteLine($"{(saveOk ? "PLANNER SAVE PASS" : "PLANNER SAVE FAIL")} · "
    + (savedLineup is null ? "没有可保存的候选" : $"“{savedLineup.Name}” {savedLineup.Slots.Count} 个槽位"));
if (!saveOk) failures++;

var plannedTrackVm = new ASPAssistant.Core.ViewModels.TrackingViewModel { TrackMultipleLineups = true };
if (trackedLineup is not null) plannedTrackVm.TrackLineup(trackedLineup);
var plannedNames = savedLineup?.Slots.Select(s => s.OperatorName).ToList() ?? [];
var trackOk = trackedLineup is not null
    && plannedNames.All(n => plannedTrackVm.TrackedOperators.Any(e => e.Name == n));
Console.WriteLine($"{(trackOk ? "PLANNER TRACK PASS" : "PLANNER TRACK FAIL")} · "
    + $"{plannedNames.Count} 名干员加入追踪");
if (!trackOk) failures++;

// 被 Ban 的核心盟约不能作为目标。
var bannedResult = planner.Plan(new PlannerRequest
{
    Goals = [new PlannerGoal("乌萨斯", 1, 3, false, true)],
    BannedCovenants = new HashSet<string> { "乌萨斯" },
});
var banMessageOk = bannedResult.Error?.Contains("乌萨斯") == true && !bannedResult.AllLineups.Any();
Console.WriteLine($"{(banMessageOk ? "PLANNER BAN PASS" : "PLANNER BAN FAIL")} · {bannedResult.Error}");
if (!banMessageOk) failures++;

// 本局 Ban 识别 → 规划器自动取消被禁的核心目标（截图识别和规划页的联动）。
plannerBans.SetCovenantBans(["乌萨斯"]);
await (plannerVm.PendingSearch ?? Task.CompletedTask);
var bannedTile = plannerVm.CoreCovenants.First(c => c.Name == "乌萨斯");
var dropOk = bannedTile.IsBlocked && bannedTile.Level == 0
    && plannerVm.GoalChips.All(chip => !chip.StartsWith("乌萨斯"));
Console.WriteLine($"{(dropOk ? "PLANNER BAN SYNC PASS" : "PLANNER BAN SYNC FAIL")} · "
    + $"{bannedTile.Name} 禁用={bannedTile.IsBanned} 已选={bannedTile.Level} 剩余目标={plannerVm.SelectedGoalCount}");
if (!dropOk) failures++;
plannerBans.ClearBans();

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
