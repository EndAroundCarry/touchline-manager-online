using FluentAssertions;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Tests.Fans;

/// <summary>
/// The words the fans' mood is shown as (`FAN-2`).
/// </summary>
public sealed class FanMoodTests
{
    [Theory]
    [InlineData(0, FanMoodWord.Furious)]
    [InlineData(999, FanMoodWord.Furious)]
    [InlineData(1_000, FanMoodWord.Angry)]
    [InlineData(2_499, FanMoodWord.Angry)]
    [InlineData(2_500, FanMoodWord.Unhappy)]
    [InlineData(3_999, FanMoodWord.Unhappy)]
    [InlineData(4_000, FanMoodWord.Calm)]
    [InlineData(5_000, FanMoodWord.Calm)]
    [InlineData(5_999, FanMoodWord.Calm)]
    [InlineData(6_000, FanMoodWord.Content)]
    [InlineData(7_499, FanMoodWord.Content)]
    [InlineData(7_500, FanMoodWord.Delighted)]
    [InlineData(8_999, FanMoodWord.Delighted)]
    [InlineData(9_000, FanMoodWord.Euphoric)]
    [InlineData(10_000, FanMoodWord.Euphoric)]
    public void Each_band_includes_its_lower_edge(int moodBp, FanMoodWord expected) =>
        FanMoods.WordFor(moodBp).Should().Be(expected);

    [Fact]
    public void A_new_clubs_fans_are_calm()
    {
        FanMoods.WordFor(FanRuleSet.CalmMoodBp).Should().Be(FanMoodWord.Calm);
        FanMoods.WordFor(FanRuleSet.OpeningFor(1).MoodBp).Should().Be(FanMoodWord.Calm);
    }

    [Fact]
    public void A_happier_mood_never_reads_as_a_lower_word()
    {
        var words = Enumerable.Range(0, FanRuleSet.MoodMaxBp + 1).Select(FanMoods.WordFor).ToList();

        words.Should().BeInAscendingOrder();
        words.Distinct().Should().BeEquivalentTo(Enum.GetValues<FanMoodWord>(), "every word is reachable");
    }

    [Fact]
    public void Every_word_has_a_phrase_and_a_unique_lower_case_code()
    {
        var words = Enum.GetValues<FanMoodWord>();

        words.Select(FanMoods.Phrase).Should().OnlyHaveUniqueItems().And.OnlyContain(phrase => phrase.Length > 0);
        words.Select(FanMoods.Code).Should().OnlyHaveUniqueItems().And.OnlyContain(code => code.All(char.IsAsciiLetterLower));
        FanMoods.Phrase(FanMoodWord.Calm).Should().Be("Calm");
        FanMoods.Code(FanMoodWord.Furious).Should().Be("furious");
    }
}
