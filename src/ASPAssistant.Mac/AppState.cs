// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.ObjectModel;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using Avalonia.Threading;

namespace ASPAssistant.Mac;

/// <summary>
/// Application-wide state for the macOS build: game data, the shared view models,
/// the connection service and the shop scanner. Mirrors the wiring of the WPF
/// <c>App.StartupAsync</c> for the features the macOS build supports.
/// </summary>
public sealed class AppState : IDisposable
{
    public static AppState Current { get; private set; } = null!;

    public required string DataDir { get; init; }
    public required string SettingsDir { get; init; }
    public required SettingsManager Settings { get; init; }
    public required IReadOnlyList<Operator> Operators { get; init; }
    public required IReadOnlyList<Equipment> Equipment { get; init; }
    public required IReadOnlyList<CovenantInfo> Covenants { get; init; }
    public required IReadOnlyList<Strategy> Strategies { get; init; }
    public required CovenantCalculator Calculator { get; init; }
    public required LineupStore LineupStore { get; init; }

    public required OperatorBrowseViewModel OperatorVm { get; init; }
    public required EquipmentBrowseViewModel EquipmentVm { get; init; }
    public required TrackingViewModel TrackingVm { get; init; }
    public required LineupViewModel LineupVm { get; init; }
    public required BanViewModel BanVm { get; init; }
    public required GameInfoViewModel GameInfo { get; init; }

    /// <summary>Reads the in-game info page (magnifier menu) into bans and enemy data.</summary>
    public BanScreenAnalyzer? Analyzer { get; private set; }

    public GameConnectionService Connection { get; private set; } = null!;
    public ScreenCaptureService Capture { get; private set; } = null!;
    public ShopScanService Scanner { get; set; } = null!;

    /// <summary>Theme used by the shop recogniser (name-strip layout).</summary>
    public GameShopTheme ShopTheme { get; set; } = GameShopTheme.Terminal;

    public bool IsDarkTheme { get; set; } = true;

    /// <summary>Raised when tracking entries changed and settings should be persisted.</summary>
    public event Action? TrackingChanged;

    public IEnumerable<string> TrackedNames =>
        TrackingVm.TrackedOperators.Select(e => e.Name).Concat(TrackingVm.TrackedEquipment.Select(e => e.Name));

    public void NotifyTrackingChanged() => TrackingChanged?.Invoke();

    public async Task SaveTrackingAsync()
    {
        try
        {
            await Settings.SaveTrackingStateAsync(TrackingVm.CaptureState());
            await Settings.SaveTrackMultipleLineupsAsync(TrackingVm.TrackMultipleLineups);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Mac", $"Could not save tracking: {ex.Message}");
        }
    }

    public static async Task<AppState> CreateAsync(string dataDir, string settingsDir)
    {
        AppLogger.Initialize(Path.Combine(settingsDir, "logs"));
        AppLogger.Info("Mac", $"data={dataDir} settings={settingsDir}");
        Directory.CreateDirectory(settingsDir);

        var store = new JsonDataStore(dataDir);
        var settings = new SettingsManager(settingsDir);

        var operators = await store.LoadOperatorsAsync();
        var custom = await store.LoadCustomOperatorsAsync();
        var selectedCustom = (await settings.LoadCustomOperatorSelectionAsync()).ToHashSet(StringComparer.Ordinal);
        foreach (var op in custom) op.IsSelectedCustom = selectedCustom.Contains(op.Name);
        operators.AddRange(custom);

        var (equipment, manualJobChange) = await store.LoadEquipmentAsync();
        var (covenants, jobChangeMap) = await store.LoadCovenantsAsync();
        var strategies = await store.LoadStrategiesAsync();

        var calculator = new CovenantCalculator(operators, covenants, jobChangeMap);
        var trackingState = await settings.LoadTrackingStateAsync(calculator.EquipmentRules);
        var lineupStore = new LineupStore(Path.Combine(settingsDir, "lineups"), calculator.EquipmentRules);
        var lineups = await lineupStore.LoadAllAsync();

        var banVm = new BanViewModel();
        var operatorVm = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default };
        operatorVm.LoadOperators(operators.Where(o => o.RequiredStrategyId.Length == 0).ToList());
        operatorVm.SetBanChecker(banVm.IsBanned);
        operatorVm.SetCovenantBanChecker(banVm.IsCovenantBanned);

        var covenantNames = operators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment, covenantNames, manualJobChange, jobChangeMap);

        var trackingVm = new TrackingViewModel
        {
            TrackMultipleLineups = await settings.LoadTrackMultipleLineupsAsync()
        };
        trackingVm.LoadState(trackingState);

        var strategyById = strategies.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var lineup in lineups)
        {
            lineup.RecommendedStrategies = lineup.Strategies
                .Select(slot => strategyById.TryGetValue(slot.StrategyId, out var strategy)
                    ? new RecommendedStrategy { Strategy = strategy, Tags = slot.Tags }
                    : null)
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderBy(s => s.Tags.FirstOrDefault() switch
                {
                    StrategyTag.阵容核心 => 0,
                    StrategyTag.优选 => 1,
                    StrategyTag.次选 => 2,
                    _ => 3
                })
                .ToList();
        }

        var lineupVm = new LineupViewModel(lineupStore, calculator);
        lineupVm.LoadLineups(lineups);

        var shopTheme = await settings.LoadGameShopThemeAsync() ?? GameShopTheme.Terminal;
        var connectionSettings = await settings.LoadGameConnectionAsync();
        // A Windows desktop window cannot be captured here; PlayCover is the default on macOS.
        if (!OperatingSystem.IsWindows() && connectionSettings.Source == GameCaptureSource.Pc)
            connectionSettings = connectionSettings with { Source = GameCaptureSource.PlayCover };
        var connection = new GameConnectionService(connectionSettings);

        var shopNames = operators.Where(o => o.RequiredStrategyId.Length == 0).Select(o => o.Name)
            .Concat(equipment.Select(e => e.Name)).Distinct().ToList();

        var state = new AppState
        {
            DataDir = dataDir,
            SettingsDir = settingsDir,
            Settings = settings,
            Operators = operators,
            Equipment = equipment,
            Covenants = covenants,
            Strategies = strategies,
            Calculator = calculator,
            LineupStore = lineupStore,
            OperatorVm = operatorVm,
            EquipmentVm = equipmentVm,
            TrackingVm = trackingVm,
            LineupVm = lineupVm,
            BanVm = banVm,
            GameInfo = new GameInfoViewModel(),
            Connection = connection,
            Capture = new ScreenCaptureService(connection),
            ShopTheme = shopTheme,
        };

        var ocr = new MaaOcrEngine(dataDir);
        var reader = new ShopReader(ocr, shopNames) { Theme = shopTheme };
        var detector = new GameShopThemeDetector(Path.Combine(dataDir, "shop_theme_refs.json"));
        var scanner = new ShopScanService(
            capture: () => state.Capture.CaptureScreen(),
            reader: reader,
            themeDetector: detector,
            catalogNames: () => shopNames);
        state.Scanner = scanner;
        scanner.AutomaticThemeDetection = await settings.LoadAutomaticGameThemeAsync();

        // ── 本局信息识别（Ban 位 / 敌方领袖）───────────────────────────────
        var bossData = new BossDataService();
        await bossData.LoadAsync(dataDir);
        var mode = new Core.GameModes.GarrisonProtocol.GarrisonProtocolMode();
        var coreCovenantNames = covenants
            .Where(c => string.Equals(c.BondType, "SEASON", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        var additionalCovenantNames = covenants
            .Where(c => !string.Equals(c.BondType, "SEASON", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        var analyzer = new BanScreenAnalyzer(
            () => Task.Run(() => state.Capture.CaptureScreen()),
            ocr, mode.OcrStrategy,
            new EnemyRecognizer(ocr, mode.OcrStrategy, bossData),
            operators, coreCovenantNames, additionalCovenantNames);
        analyzer.ScrollInformationPanelAsync = async () =>
        {
            var scrolled = await Task.Run(() => state.Connection.ScrollInformationPanelDown());
            if (!scrolled) return null;
            // Let the panel settle: a short step plus a longer pause keeps rows from being skipped.
            await Task.Delay(400);
            return await Task.Run(() => state.Capture.CaptureScreen());
        };
        analyzer.AnalysisBusyChanged += busy => Dispatcher.UIThread.Post(() =>
        {
            if (busy) state.GameInfo.Begin();
        });
        analyzer.BanGroupsComputed += groups => Dispatcher.UIThread.Post(() => state.GameInfo.SetGroups(groups));
        analyzer.AutoBanListComputed += names => Dispatcher.UIThread.Post(() =>
        {
            state.BanVm.SetBans(names);
            state.GameInfo.SetBannedOperators(names);
            state.OperatorVm.NotifyBansChanged();
        });
        analyzer.AnalysisCompleted += result => Dispatcher.UIThread.Post(() =>
        {
            state.BanVm.SetCovenantBans(result.CoreCovenants.Concat(result.AdditionalCovenants)
                .Where(c => c.IsBanned).Select(c => c.Name));
            state.OperatorVm.SetCovenantBanChecker(state.BanVm.IsCovenantBanned);
            state.OperatorVm.SetAvailableCovenants(result.CoreCovenants.Concat(result.AdditionalCovenants)
                .Where(c => !c.IsBanned).Select(c => c.Name));
            state.GameInfo.Apply(result);
            state.OperatorVm.NotifyBansChanged();
            state.BanListChanged?.Invoke();
        });
        analyzer.AnalysisFailed += message => Dispatcher.UIThread.Post(() => state.GameInfo.Fail(message));
        analyzer.EnemyDetected += enemy => Dispatcher.UIThread.Post(() => state.GameInfo.SetEnemy(enemy));
        state.Analyzer = analyzer;

        // Auto-detection: while the shop scan finds nothing, check whether the info page is open
        // (both covenant headers visible). The frame hash keeps one page from being analysed twice.
        var informationDetector = new InformationScreenDetector(ocr, mode.OcrStrategy);
        state.AutoDetectGameInfo = await settings.LoadAutoDetectGameInfoAsync();
        scanner.NoShopFrameAsync = async (frame, width, height) =>
        {
            if (!state.AutoDetectGameInfo) return;
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame));
            if (hash == state._lastInfoHash) return;
            if (!await informationDetector.IsInformationScreenAsync(frame, width, height)) return;
            state._lastInfoHash = hash;
            await state.IdentifyGameInfoAsync();
        };
        scanner.ThemeDetected += theme =>
        {
            reader.Theme = theme;
            state.ShopTheme = theme;
        };
        scanner.ItemsRecognized += items => state.LastShopItems = items;

        state.ShowOverlay = LoadShowOverlay(settingsDir);
        Current = state;
        return state;
    }

    /// <summary>Replaces the connection chosen in the settings page and persists it.</summary>
    public async Task ApplyConnectionAsync(GameConnectionSettings settings)
    {
        var previous = Connection;
        Capture.SetConnection(new GameConnectionService(settings));
        Connection = Capture.Connection;
        previous.Dispose();
        await Settings.SaveGameConnectionAsync(settings);
    }

    /// <summary>The experimental overlay window, when shown.</summary>
    public Views.OverlayWindow? Overlay { get; set; }

    /// <summary>Whether the overlay was enabled last time (persisted in mac-overlay.json).</summary>
    public bool ShowOverlay { get; set; }

    public async Task SetShowOverlayAsync(bool show)
    {
        ShowOverlay = show;
        try
        {
            Directory.CreateDirectory(SettingsDir);
            await File.WriteAllTextAsync(Path.Combine(SettingsDir, "mac-overlay.json"),
                System.Text.Json.JsonSerializer.Serialize(show));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Mac", $"Could not save overlay setting: {ex.Message}");
        }
    }

    private static bool LoadShowOverlay(string settingsDir)
    {
        try
        {
            var path = Path.Combine(settingsDir, "mac-overlay.json");
            return File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(path));
        }
        catch { return false; }
    }

    /// <summary>Raised after bans changed, so the operator page can rebuild its rows.</summary>
    public event Action? BanListChanged;

    /// <summary>Recognise the in-game info page automatically when it appears (default on).</summary>
    public bool AutoDetectGameInfo { get; set; } = true;

    private string? _lastInfoHash;

    public async Task IdentifyGameInfoAsync()
    {
        if (Analyzer is null) return;
        // The shop scanner and the ban analyzer share one MaaTasker/OCR engine; stop sampling
        // while the info page is being read, then resume.
        var wasScanning = Scanner.IsRunning;
        Scanner.Stop();
        try { await Analyzer.RunAnalysisAsync(); }
        catch (Exception ex) { AppLogger.Warn("Mac", $"Game info analysis failed: {ex.Message}"); }
        finally { if (wasScanning) Scanner.Start(); }
    }

    /// <summary>Changes the shop theme used by the recogniser and persists it.</summary>
    public async Task SetAutoDetectGameInfoAsync(bool enabled)
    {
        AutoDetectGameInfo = enabled;
        try { await Settings.SaveAutoDetectGameInfoAsync(enabled); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Mac", $"Could not save auto-detect setting: {ex.Message}");
        }
    }

    public async Task SetShopThemeAsync(GameShopTheme theme)
    {
        ShopTheme = theme;
        Scanner.ApplyTheme(theme);
        try { await Settings.SaveGameShopThemeAsync(theme); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Mac", $"Could not save shop theme: {ex.Message}");
        }
    }

    /// <summary>Items from the most recent successful shop scan.</summary>
    public IReadOnlyList<ShopItem> LastShopItems { get; set; } = [];

    public void Dispose()
    {
        Scanner.Dispose();
        Connection.Dispose();
    }
}
