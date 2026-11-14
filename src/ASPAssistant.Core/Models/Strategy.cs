// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>Strategy recommended by a lineup, with the tags recorded in the lineup data.</summary>
public sealed class RecommendedStrategy
{
    public required Strategy Strategy { get; init; }
    public List<StrategyTag> Tags { get; init; } = [];

    public string TagText => string.Join(" · ", Tags);

    public string Tooltip => Strategy.Description
        + (Strategy.InitialHp > 0 ? $"\n初始生命：{Strategy.InitialHp}" : "")
        + (string.IsNullOrWhiteSpace(Strategy.UnlockRequirement) ? "" : $"\n解锁：{Strategy.UnlockRequirement}")
        + (string.IsNullOrWhiteSpace(Strategy.Initiator) ? "" : $"\n发起人：{Strategy.Initiator}");
}

public class Strategy
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initiator { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnlockRequirement { get; set; } = "";
    public int InitialHp { get; set; }
    public List<string> InactiveModes { get; set; } = [];
}
