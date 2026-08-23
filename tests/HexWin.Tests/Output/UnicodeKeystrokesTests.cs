using HexWin.Output;
using Xunit;

namespace HexWin.Tests.Output;

/// <summary>
/// Sending happens in Unicode and not in key codes, which makes the injection
/// independent of the keyboard layout. On a French keyboard, "a" and "q" are
/// not where a program would expect them, and "é" is on no key at all of a US
/// keyboard.
/// </summary>
public class UnicodeKeystrokesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_text_produces_no_keystroke(string? text)
    {
        Assert.Empty(UnicodeKeystrokes.Build(text));
    }

    [Fact]
    public void Each_character_gives_a_press_then_a_release()
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build("ab");

        Assert.Equal(4, strokes.Length);
        Assert.Equal(new Keystroke('a', IsKeyUp: false), strokes[0]);
        Assert.Equal(new Keystroke('a', IsKeyUp: true), strokes[1]);
        Assert.Equal(new Keystroke('b', IsKeyUp: false), strokes[2]);
        Assert.Equal(new Keystroke('b', IsKeyUp: true), strokes[3]);
    }

    [Fact]
    public void Accented_letters_pass_through_as_they_are()
    {
        // The whole point of the approach: "é" has no key code on a US
        // keyboard, but its Unicode code unit is universal.
        Keystroke[] strokes = UnicodeKeystrokes.Build("é");

        Assert.Equal(2, strokes.Length);
        Assert.Equal('é', strokes[0].Unit);
    }

    [Fact]
    public void An_emoji_is_sent_as_two_separate_units()
    {
        // Characters outside the basic multilingual plane take two UTF-16
        // units. Sending them together would insert nothing: Windows expects
        // two keystrokes and recombines them itself.
        Keystroke[] strokes = UnicodeKeystrokes.Build("🙂");

        Assert.Equal(4, strokes.Length);
        Assert.True(char.IsHighSurrogate((char)strokes[0].Unit));
        Assert.True(char.IsLowSurrogate((char)strokes[2].Unit));
    }

    [Fact]
    public void A_line_ending_becomes_the_Enter_key()
    {
        // Sent as Unicode, a line break inserts nothing at all: the real key
        // is needed.
        Keystroke[] strokes = UnicodeKeystrokes.Build("\n");

        Assert.Equal(2, strokes.Length);
        Assert.True(UnicodeKeystrokes.IsReturn(strokes[0]));
        Assert.False(strokes[0].IsKeyUp);
        Assert.True(strokes[1].IsKeyUp);
    }

    [Fact]
    public void A_Windows_line_ending_produces_a_single_Enter_key()
    {
        // Without this rule, "\r\n" would insert two line breaks.
        Keystroke[] strokes = UnicodeKeystrokes.Build("a\r\nb");

        Assert.Equal(6, strokes.Length);
        Assert.Single(strokes, s => UnicodeKeystrokes.IsReturn(s) && !s.IsKeyUp);
    }

    [Fact]
    public void A_full_sentence_keeps_its_order()
    {
        const string sentence = "Bonjour Marie, à demain !";

        Keystroke[] strokes = UnicodeKeystrokes.Build(sentence);
        string rebuilt = new([.. strokes.Where(s => !s.IsKeyUp).Select(s => (char)s.Unit)]);

        Assert.Equal(sentence, rebuilt);
    }

    [Fact]
    public void Spaces_and_punctuation_are_not_treated_specially()
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build(" ,");

        Assert.Equal(4, strokes.Length);
        Assert.Equal(' ', strokes[0].Unit);
        Assert.Equal(',', strokes[2].Unit);
    }
}
