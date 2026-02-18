internal sealed class CollectorSettings
{
    // 互換性のために残す（旧設定名）。
    public int PollingIntervalSeconds { get; init; } = 0;
    public int RescanIntervalSeconds { get; init; } = 300;
    public string SqliteFilePath { get; init; } = Path.Combine("data", "collector.db");

    public string[] ExcludedExeNames { get; init; } =
    [
        "dwm.exe",
        "TextInputHost.exe",
        "NVIDIA Overlay.exe",
        "Overwolf.exe",
        "ArmourySwAgent.exe"
    ];
}
