using System.Globalization;

namespace HexWin.Configuration;

/// <summary>
/// Reads a colour written the way a human writes one, <c>#RRGGBB</c>.
///
/// Deliberately kept clear of System.Drawing: the validation in
/// <see cref="AppSettings"/> has to stay pure, testable with no desktop and no
/// graphics stack behind it. The packed value it returns is what
/// <c>Color.FromArgb</c> takes anyway.
/// </summary>
public static class HexColor
{
    private const int Digits = 6;

    /// <summary>
    /// Parses <c>#RRGGBB</c> into a packed <c>0xRRGGBB</c> value. The hash is
    /// optional and the case irrelevant: both are things a user gets wrong
    /// without being wrong.
    /// </summary>
    public static bool TryParse(string? value, out int rgb)
    {
        rgb = 0;

        ReadOnlySpan<char> digits = value.AsSpan().Trim();

        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        if (digits.Length != Digits)
        {
            return false;
        }

        return int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>Writes a packed value back as <c>#RRGGBB</c>.</summary>
    public static string ToHex(int rgb) =>
        string.Create(CultureInfo.InvariantCulture, $"#{rgb:X6}");
}
