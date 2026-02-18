internal readonly record struct AppInterval(
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc,
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State);
