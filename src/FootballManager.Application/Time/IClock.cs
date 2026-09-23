namespace FootballManager.Application.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
