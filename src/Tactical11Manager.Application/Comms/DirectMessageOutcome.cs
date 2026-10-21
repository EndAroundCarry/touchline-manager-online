namespace Tactical11Manager.Application.Comms;

/// <summary>What a direct-message read or command did (`COM-10` to `COM-16`).</summary>
public enum DirectMessageOutcome
{
    /// <summary>The read or command succeeded.</summary>
    Ok = 0,

    /// <summary>The account has no manager profile, so it has no conversations.</summary>
    NoManagerProfile = 1,

    /// <summary>The sender holds no club, and a conversation is started from one (`COM-11`).</summary>
    SenderHasNoClub = 2,

    /// <summary>No club exists with the requested identity.</summary>
    ClubNotFound = 3,

    /// <summary>The club has no human manager, so there is nobody to write to (`COM-11`).</summary>
    ClubHasNoManager = 4,

    /// <summary>The club is the sender's own (`COM-11`).</summary>
    OwnClub = 5,

    /// <summary>The recipient's account is being closed (`COM-11`, `COM-12`).</summary>
    RecipientUnavailable = 6,

    /// <summary>A block stands between the two managers, in either direction (`COM-15`).</summary>
    Blocked = 7,

    /// <summary>No conversation exists with the requested identity for this manager.</summary>
    ConversationNotFound = 8,

    /// <summary>The subject or message is empty, or too long after trimming (`COM-13`).</summary>
    InvalidText = 9,

    /// <summary>The supplied page cursor is not one this build produced.</summary>
    InvalidCursor = 10,

    /// <summary>No manager exists with the requested identity, or it is the caller's own.</summary>
    ManagerNotFound = 11,
}
