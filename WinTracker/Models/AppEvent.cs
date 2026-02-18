internal readonly record struct AppEvent(
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc,
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State,
    string Source);
