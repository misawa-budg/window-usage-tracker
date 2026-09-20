namespace WinTracker.Shared.Analytics;

// Stable colors and labels, independent of interval selection and Windows UI types.
public static class TimelinePresentation
{
    public static string FormatBucketLabel(DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
    {
        DateTimeOffset localStart = bucketStartUtc.ToLocalTime();
        DateTimeOffset localEnd = bucketEndUtc.ToLocalTime();
        TimeSpan bucket = bucketEndUtc - bucketStartUtc;
        if (bucket >= TimeSpan.FromHours(23))
        {
            return $"{localStart:MM/dd (ddd)}";
        }

        return $"{localStart:MM/dd HH:mm} - {localEnd:HH:mm}";
    }

    public static string ToDuration(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}";
    }

    public static string FormatTooltipTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        string startStr = start.ToString("HH:mm:ss");
        string endStr = end.ToString("HH:mm:ss");
        if (end > start && end.TimeOfDay.Ticks == 0)
        {
            endStr = "24:00:00";
        }
        return $"{startStr}-{endStr}";
    }

    public static string ToDurationWithSeconds(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }

    public static string ColorForKey(string key)
    {
        uint hash = ComputeStableHash(key);
        int hue = (int)(hash % 360);
        double saturation = 0.46 + (((hash >> 8) % 15) / 100.0);
        double lightness = 0.56 + (((hash >> 16) % 11) / 100.0);
        (byte r, byte g, byte b) = HslToRgb(hue / 360.0, saturation, lightness);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static string ColorForAppState(string appName, string state)
    {
        string appColor = ColorForKey(appName);
        return ColorForStateTone(appColor, state);
    }

    private static uint ComputeStableHash(string key)
    {
        uint hash = 2166136261;
        foreach (char c in key.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    private static string ColorForStateTone(string baseColorHex, string state)
    {
        if (string.Equals(state, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return BlendWithWhite(baseColorHex, 0.30);
        }

        if (string.Equals(state, "Minimized", StringComparison.OrdinalIgnoreCase))
        {
            return BlendWithWhite(baseColorHex, 0.70);
        }

        return baseColorHex;
    }

    private static string BlendWithWhite(string hexColor, double ratio)
    {
        if (!TryParseHexColor(hexColor, out byte r, out byte g, out byte b))
        {
            return hexColor;
        }

        double clamped = Math.Clamp(ratio, 0, 1);
        byte rr = BlendChannel(r, clamped);
        byte gg = BlendChannel(g, clamped);
        byte bb = BlendChannel(b, clamped);
        return $"#{rr:X2}{gg:X2}{bb:X2}";
    }

    private static bool TryParseHexColor(string hexColor, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;
        if (hexColor.Length != 7 || !hexColor.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            r = Convert.ToByte(hexColor.Substring(1, 2), 16);
            g = Convert.ToByte(hexColor.Substring(3, 2), 16);
            b = Convert.ToByte(hexColor.Substring(5, 2), 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte BlendChannel(byte value, double ratio) =>
        (byte)Math.Round((value * (1 - ratio)) + (255 * ratio));

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h * 6) % 2 - 1));
        double m = l - c / 2;

        double r1;
        double g1;
        double b1;

        if (h < 1.0 / 6.0)
        {
            r1 = c;
            g1 = x;
            b1 = 0d;
        }
        else if (h < 2.0 / 6.0)
        {
            r1 = x;
            g1 = c;
            b1 = 0d;
        }
        else if (h < 3.0 / 6.0)
        {
            r1 = 0d;
            g1 = c;
            b1 = x;
        }
        else if (h < 4.0 / 6.0)
        {
            r1 = 0d;
            g1 = x;
            b1 = c;
        }
        else if (h < 5.0 / 6.0)
        {
            r1 = x;
            g1 = 0d;
            b1 = c;
        }
        else
        {
            r1 = c;
            g1 = 0d;
            b1 = x;
        }

        byte r = (byte)Math.Round((r1 + m) * 255);
        byte g = (byte)Math.Round((g1 + m) * 255);
        byte b = (byte)Math.Round((b1 + m) * 255);
        return (r, g, b);
    }
}
