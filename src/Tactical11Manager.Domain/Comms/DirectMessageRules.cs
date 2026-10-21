using System.Text;

namespace Tactical11Manager.Domain.Comms;

/// <summary>
/// The limits and the text rules of a direct message between two managers (`COM-10`, `COM-13`).
/// </summary>
/// <remarks>
/// The rules live in one place so the domain, the request validation and the database column agree on one
/// number. Text is stored as the manager wrote it and is always shown as text, never as markup: the only
/// changes made here are trimming, a uniform line break, and removing control characters that have no place
/// in a sentence.
/// </remarks>
public static class DirectMessageRules
{
    /// <summary>The longest subject, in characters after trimming (`dm_subject_max_chars`).</summary>
    public const int SubjectMaxChars = 80;

    /// <summary>The longest message body, in characters after trimming (`dm_body_max_chars`).</summary>
    public const int BodyMaxChars = 1000;

    /// <summary>The most messages a manager may send in an hour, starts and replies together (`dm_sends_per_hour`).</summary>
    public const int SendsPerHour = 30;

    /// <summary>The number of conversations a list page holds.</summary>
    public const int ConversationPageSize = 25;

    /// <summary>The number of messages a thread page holds.</summary>
    public const int ThreadPageSize = 50;

    /// <summary>
    /// Normalizes a subject: trims it, turns every line break into a space (a subject is one line), and removes
    /// control characters.
    /// </summary>
    /// <param name="subject">What the manager typed.</param>
    /// <returns>The text to store, or null when nothing valid remains or it is too long.</returns>
    public static string? NormalizeSubject(string? subject)
    {
        var text = Clean(subject, keepLineBreaks: false);

        return text.Length is >= 1 and <= SubjectMaxChars ? text : null;
    }

    /// <summary>
    /// Normalizes a body: trims it, makes every line break a bare line feed, and removes control characters
    /// other than the line break.
    /// </summary>
    /// <param name="body">What the manager typed.</param>
    /// <returns>The text to store, or null when nothing valid remains or it is too long.</returns>
    public static string? NormalizeBody(string? body)
    {
        var text = Clean(body, keepLineBreaks: true);

        return text.Length is >= 1 and <= BodyMaxChars ? text : null;
    }

    private static string Clean(string? value, bool keepLineBreaks)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character == '\r')
            {
                // A carriage return followed by a line feed is one break, not two.
                if (index + 1 < value.Length && value[index + 1] == '\n')
                {
                    continue;
                }

                character = '\n';
            }

            if (character == '\n')
            {
                builder.Append(keepLineBreaks ? '\n' : ' ');
                continue;
            }

            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }
}
