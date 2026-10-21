namespace Tactical11Manager.Domain.Fans;

/// <summary>How the fans feel, in a word (`FAN-2`).</summary>
/// <remarks>The members run from the least to the most satisfied, so a higher value is always a happier crowd.</remarks>
public enum FanMoodWord
{
    /// <summary>Mood below 10 on the 0-100 scale.</summary>
    Furious = 0,

    /// <summary>Mood from 10 to 25.</summary>
    Angry = 1,

    /// <summary>Mood from 25 to 40.</summary>
    Unhappy = 2,

    /// <summary>Mood from 40 to 60: where a new club starts.</summary>
    Calm = 3,

    /// <summary>Mood from 60 to 75.</summary>
    Content = 4,

    /// <summary>Mood from 75 to 90.</summary>
    Delighted = 5,

    /// <summary>Mood of 90 or more.</summary>
    Euphoric = 6,
}

/// <summary>The words and stored codes of the fans' moods.</summary>
public static class FanMoods
{
    /// <summary>The word for a mood: each band includes its lower edge, so a mood of exactly 25 is unhappy, not angry.</summary>
    /// <param name="moodBp">The mood, from 0 to 10,000 basis points.</param>
    public static FanMoodWord WordFor(int moodBp) => moodBp switch
    {
        < 1_000 => FanMoodWord.Furious,
        < 2_500 => FanMoodWord.Angry,
        < 4_000 => FanMoodWord.Unhappy,
        < 6_000 => FanMoodWord.Calm,
        < 7_500 => FanMoodWord.Content,
        < 9_000 => FanMoodWord.Delighted,
        _ => FanMoodWord.Euphoric,
    };

    /// <summary>The word the game shows for a mood.</summary>
    /// <param name="word">The mood word.</param>
    public static string Phrase(FanMoodWord word) => word switch
    {
        FanMoodWord.Furious => "Furious",
        FanMoodWord.Angry => "Angry",
        FanMoodWord.Unhappy => "Unhappy",
        FanMoodWord.Calm => "Calm",
        FanMoodWord.Content => "Content",
        FanMoodWord.Delighted => "Delighted",
        FanMoodWord.Euphoric => "Euphoric",
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Unknown fan mood word."),
    };

    /// <summary>The stable code a mood word is sent under.</summary>
    /// <param name="word">The mood word.</param>
    public static string Code(FanMoodWord word) => word switch
    {
        FanMoodWord.Furious => "furious",
        FanMoodWord.Angry => "angry",
        FanMoodWord.Unhappy => "unhappy",
        FanMoodWord.Calm => "calm",
        FanMoodWord.Content => "content",
        FanMoodWord.Delighted => "delighted",
        FanMoodWord.Euphoric => "euphoric",
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Unknown fan mood word."),
    };
}
