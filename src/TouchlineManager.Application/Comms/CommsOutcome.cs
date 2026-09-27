namespace TouchlineManager.Application.Comms;

/// <summary>What an inbox read or command did (master plan §10.7).</summary>
public enum CommsOutcome
{
    /// <summary>The read or command succeeded.</summary>
    Ok = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile, so it has no inbox.</summary>
    NoManagerProfile = 2,

    /// <summary>No message exists with the requested identity for this manager.</summary>
    MessageNotFound = 3,

    /// <summary>The supplied page cursor is not one this build produced.</summary>
    InvalidCursor = 4,
}
