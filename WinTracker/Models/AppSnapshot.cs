internal readonly record struct AppSnapshot(
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State);
