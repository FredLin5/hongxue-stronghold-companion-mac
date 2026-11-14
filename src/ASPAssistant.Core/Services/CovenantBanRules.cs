// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Which operators a run's covenant bans remove from the pool. Used by both the ban-screen analysis
/// (干员页的本局不可用) and the planner, so the two always agree.
/// </summary>
public static class CovenantBanRules
{
    /// <summary>
    /// 核心盟约是干员的阵营：只要有一个核心盟约被 ban，这个干员本局就不可用。
    /// 例如锏同时属于卡西米尔 / 谢拉格 / 莱塔尼亚，ban 掉莱塔尼亚后锏同样被禁用（游戏内实测）。
    ///
    /// 没有核心盟约的干员（数据里有 49 个，例如霍尔海雅只有奥术 + 精准）没有阵营兜底，
    /// 只有全部附加盟约都被 ban 时才会退出池子。
    /// </summary>
    public static bool IsExcluded(Operator op, IReadOnlySet<string> bannedCovenants)
        => IsExcluded(op.CoreCovenants, op.AdditionalCovenants, bannedCovenants);

    public static bool IsExcluded(IEnumerable<string> core, IEnumerable<string> additional, IReadOnlySet<string> bannedCovenants)
    {
        if (bannedCovenants.Count == 0) return false;

        var coreList = core.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        if (coreList.Count > 0)
            return coreList.Any(bannedCovenants.Contains);

        var additionalList = additional.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        return additionalList.Count > 0 && additionalList.All(bannedCovenants.Contains);
    }
}
