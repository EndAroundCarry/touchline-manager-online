using System.Globalization;
using System.Text.Json;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// The weekly fan review's schedule and its job: one row a week, due on Sunday at <see cref="FanRuleSet.WeeklyReviewUtc"/> (`FAN-8`).
/// </summary>
/// <remarks>
/// A weekly row keyed on the Sunday it is for, the same shape the weekly finance run uses: the row is the deadline, so a worker that was
/// down at the boundary runs the review late rather than skipping it, and the review's own once-a-week-per-club rule makes the
/// at-least-once queue safe.
/// </remarks>
public static class FanReviewWeeks
{
    /// <summary>The job type that reviews every club's fans for a week.</summary>
    public const string JobType = "fans.weekly-review";

    /// <summary>Gets the most recent review boundary at or before an instant.</summary>
    /// <param name="now">The instant.</param>
    public static DateTimeOffset BoundaryAtOrBefore(DateTimeOffset now)
    {
        var boundary = BoundaryOn(now.UtcDateTime.Date);

        if (now < boundary)
        {
            boundary = boundary.AddDays(-1);
        }

        while (boundary.DayOfWeek != DayOfWeek.Sunday)
        {
            boundary = boundary.AddDays(-1);
        }

        return boundary;
    }

    /// <summary>Gets the first review boundary at or after an instant.</summary>
    /// <param name="now">The instant.</param>
    public static DateTimeOffset BoundaryAtOrAfter(DateTimeOffset now)
    {
        var boundary = BoundaryAtOrBefore(now);

        return boundary < now ? boundary.AddDays(7) : boundary;
    }

    /// <summary>Gets the business key of a week's review job.</summary>
    /// <param name="weekOf">The Sunday the review is for.</param>
    public static string WeekKey(DateOnly weekOf) =>
        string.Create(CultureInfo.InvariantCulture, $"{JobType}:{weekOf:yyyy-MM-dd}");

    /// <summary>Builds the payload that names the week a job is for.</summary>
    /// <param name="weekOf">The Sunday the review is for.</param>
    public static string PayloadFor(DateOnly weekOf) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"date":"{{weekOf:yyyy-MM-dd}}"}""");

    /// <summary>Reads the week from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="weekOf">The week, when the payload names one.</param>
    /// <returns>Whether the payload named a week.</returns>
    public static bool TryReadWeek(string payloadJson, out DateOnly weekOf)
    {
        weekOf = default;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("date", out var value)
                && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out weekOf);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset BoundaryOn(DateTime date) =>
        new(date + FanRuleSet.WeeklyReviewUtc.ToTimeSpan(), TimeSpan.Zero);
}
