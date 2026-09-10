using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// A colour is typed by hand into a text file, so it arrives in whatever form
/// the person had in mind. What is accepted is deliberately wide; what is
/// rejected must be rejected cleanly, because the caller draws with the result.
/// </summary>
public class HexColorTests
{
    [Theory]
    [InlineData("#1971C2")]
    [InlineData("1971C2")]
    [InlineData("#1971c2")]
    [InlineData("  #1971C2  ")]
    public void The_forms_a_person_actually_writes_are_accepted(string value)
    {
        // The hash and the case are things one gets wrong without being wrong.
        Assert.True(HexColor.TryParse(value, out int rgb));
        Assert.Equal(0x1971C2, rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("blue")]
    [InlineData("#12 34 56")]
    [InlineData("rgb(25,113,194)")]
    public void Anything_else_is_refused(string? value)
    {
        Assert.False(HexColor.TryParse(value, out _));
    }

    [Fact]
    public void Black_and_white_are_ordinary_colours()
    {
        // The bounds of the range, and the one case where a parsed zero could be
        // mistaken for a failure.
        Assert.True(HexColor.TryParse("#000000", out int black));
        Assert.Equal(0, black);

        Assert.True(HexColor.TryParse("#FFFFFF", out int white));
        Assert.Equal(0xFFFFFF, white);
    }

    [Theory]
    [InlineData(0x1971C2, "#1971C2")]
    [InlineData(0, "#000000")]
    [InlineData(0xFFFFFF, "#FFFFFF")]
    public void A_value_is_written_back_padded_and_upper_case(int rgb, string expected)
    {
        // Padding matters: "#0000FF" written as "#FF" would not read back.
        Assert.Equal(expected, HexColor.ToHex(rgb));
    }
}
