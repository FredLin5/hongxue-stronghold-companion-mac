// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<GameCaptureSource>))]
public enum GameCaptureSource { Pc, Adb, PlayCover }

public sealed record GameConnectionSettings
{
    public GameCaptureSource Source { get; init; }
    public string AdbPath { get; init; } = "";
    public string AdbSerial { get; init; } = "";
    public string WindowTitle { get; init; } = "MuMu安卓设备";

    /// <summary>PlayTools "MaaTools" TCP service address (PlayCover 应用设置里的端口，默认 1717)。</summary>
    public string PlayCoverAddress { get; init; } = "127.0.0.1:1717";

    /// <summary>PlayCover 中安装的应用 bundle id，默认明日方舟。</summary>
    public string PlayCoverBundleId { get; init; } = "com.hypergryph.arknights";
}
