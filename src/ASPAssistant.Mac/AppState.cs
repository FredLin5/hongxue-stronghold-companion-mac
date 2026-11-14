// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.ObjectModel;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

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
            trackedNames: () => state.TrackedNames.ToList());
        state.Scanner = scanner;
        scanner.AutomaticThemeDetection = await settings.LoadAutomaticGameThemeAsync();
        scanner.ThemeDetected += theme =>
        {
            reader.Theme = theme;
            state.ShopTheme = theme;
        };
        scanner.ItemsRecognized += items => state.LastShopItems = items;

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

    /// <summary>Changes the shop theme used by the recogniser and persists it.</summary>
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
