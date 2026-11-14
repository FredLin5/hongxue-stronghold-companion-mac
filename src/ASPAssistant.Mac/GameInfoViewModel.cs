// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.ObjectModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Mac;

/// <summary>State of the "本局信息" page: bans, enemies and the last recognition status.</summary>
public partial class GameInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "打开游戏里的本局信息页（左上角放大镜）后点「识别本局信息」。";

    [ObservableProperty]
    private string _enemyBoss = "";

    [ObservableProperty]
    private string _enemyTypes = "";

    [ObservableProperty]
    private int _bannedOperatorCount;

    [ObservableProperty]
    private bool _hasResult;

    public ObservableCollection<string> BannedCoreCovenants { get; } = [];
    public ObservableCollection<string> BannedAdditionalCovenants { get; } = [];
    public ObservableCollection<string> AvailableCoreCovenants { get; } = [];
    public ObservableCollection<string> BannedOperators { get; } = [];

    /// <summary>Per-covenant counts of the ban list, for comparing against the in-game grouping.</summary>
    public ObservableCollection<string> BanGroups { get; } = [];

    public void Begin()
    {
        IsBusy = true;
        Status = "识别中…（读盟约面板与敌方领袖，约 2 秒）";
    }

    public void Fail(string message)
    {
        IsBusy = false;
        // Core keeps the Windows wording ("确认本局信息"); the in-game page is opened from the magnifier.
        Status = message.Contains("当前画面不是本局信息页", StringComparison.Ordinal)
            ? "未识别到本局信息页：请点游戏左上角（回合数左边）的放大镜打开本局信息，再点「识别本局信息」。"
            : message;
    }

    public void SetEnemy(EnemyResult enemy)
    {
        EnemyBoss = enemy.BossName;
        EnemyTypes = string.Join(" · ", enemy.EnemyTypes);
    }

    public void SetGroups(IReadOnlyList<(string Covenant, int Count)> groups)
    {
        BanGroups.Clear();
        foreach (var (covenant, count) in groups) BanGroups.Add($"{covenant} {count}");
    }

    public void SetBannedOperators(IEnumerable<string> names)
    {
        BannedOperators.Clear();
        foreach (var name in names) BannedOperators.Add(name);
        BannedOperatorCount = BannedOperators.Count;
    }

    public void Apply(BanScreenResult result)
    {
        BannedCoreCovenants.Clear();
        foreach (var covenant in result.CoreCovenants.Where(c => c.IsBanned)) BannedCoreCovenants.Add(covenant.Name);
        BannedAdditionalCovenants.Clear();
        foreach (var covenant in result.AdditionalCovenants.Where(c => c.IsBanned)) BannedAdditionalCovenants.Add(covenant.Name);
        AvailableCoreCovenants.Clear();
        foreach (var covenant in result.CoreCovenants.Where(c => !c.IsBanned)) AvailableCoreCovenants.Add(covenant.Name);

        if (result.EnemyFaction is { Length: > 0 } faction) EnemyBoss = faction;
        IsBusy = false;
        HasResult = true;
        Status = $"已录入 · 核心禁用 {BannedCoreCovenants.Count} · 附加禁用 {BannedAdditionalCovenants.Count}"
            + (result.EnemyFaction is null ? " · 敌人未读出" : "");
    }

    public void Clear()
    {
        BannedCoreCovenants.Clear();
        BannedAdditionalCovenants.Clear();
        AvailableCoreCovenants.Clear();
        BannedOperators.Clear();
        BanGroups.Clear();
        BannedOperatorCount = 0;
        EnemyBoss = "";
        EnemyTypes = "";
        HasResult = false;
        IsBusy = false;
        Status = "已清空本局信息。";
    }
}
