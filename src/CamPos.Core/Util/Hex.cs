using System.Globalization;

namespace CamPos.Core.Util;

public static class Hex
{
    public static ulong ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("地址不能为空。");
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (!ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"无效的十六进制地址: {text}");
        return value;
    }

    public static bool TryParseAddress(string? text, out ulong value)
    {
        value = 0;
        try
        {
            value = ParseAddress(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int ParseOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var s = text.Trim();
        var negative = s.StartsWith('-');
        if (negative)
            s = s[1..];
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ||
            value > (long)int.MaxValue + 1L)
            throw new FormatException($"无效的十六进制偏移: {text}");
        var signed = negative ? -value : value;
        if (signed < int.MinValue || signed > int.MaxValue)
            throw new FormatException($"偏移超出 Int32 范围: {text}");
        return (int)signed;
    }

    public static ulong AddOffset(ulong address, int offset)
    {
        return offset >= 0 ? address + (ulong)offset : address - (ulong)(-(long)offset);
    }

    public static string Addr(ulong value) => $"0x{value:X}";
}
