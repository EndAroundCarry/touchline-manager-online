using FluentAssertions;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Domain.Tests.Comms;

/// <summary>
/// Direct messages between managers: the text rules, the conversation aggregate, and reading (`COM-10`,
/// `COM-13`, `COM-16`).
/// </summary>
public sealed class DirectMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Ben = Guid.CreateVersion7();

    private static Conversation Start(string subject = "Friendly?") =>
        Conversation.Start(Guid.CreateVersion7(), subject, Alice, Ben, Guid.CreateVersion7(), Now);

    private static DirectMessage Write(string body = "Fancy a friendly?") =>
        DirectMessage.Write(Guid.CreateVersion7(), Guid.CreateVersion7(), Alice, Ben, body, Now);

    [Fact]
    public void A_new_conversation_keeps_its_two_managers_and_starts_at_version_one()
    {
        var clubId = Guid.CreateVersion7();

        var conversation = Conversation.Start(Guid.CreateVersion7(), "Friendly?", Alice, Ben, clubId, Now);

        conversation.StartedByManagerId.Should().Be(Alice);
        conversation.RecipientManagerId.Should().Be(Ben);
        conversation.StartedFromClubId.Should().Be(clubId);
        conversation.LastMessageAt.Should().Be(Now, "the thread's latest activity is its start (COM-10)");
        conversation.Version.Should().Be(1);
    }

    [Fact]
    public void A_manager_cannot_start_a_conversation_with_themselves()
    {
        var act = () => Conversation.Start(Guid.CreateVersion7(), "Hello me", Alice, Alice, Guid.CreateVersion7(), Now);

        act.Should().Throw<ArgumentException>("a conversation is between exactly two managers (COM-10)");
    }

    [Fact]
    public void A_message_cannot_go_from_a_manager_to_themselves()
    {
        var act = () => DirectMessage.Write(Guid.CreateVersion7(), Guid.CreateVersion7(), Alice, Alice, "Hi", Now);

        act.Should().Throw<ArgumentException>("a message goes between two managers (COM-10)");
    }

    [Fact]
    public void A_subject_is_trimmed_and_kept_on_one_line()
    {
        var conversation = Start("  Cup\r\ndraw \n tonight  ");

        conversation.Subject.Should().Be("Cup draw   tonight", "a subject is one trimmed line (COM-13)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void A_blank_subject_is_refused(string subject)
    {
        var act = () => Start(subject);

        act.Should().Throw<ArgumentException>("a subject needs at least one character after trimming (COM-13)");
    }

    [Fact]
    public void A_subject_of_exactly_the_limit_is_accepted_and_one_more_is_refused()
    {
        Start(new string('a', DirectMessageRules.SubjectMaxChars)).Subject
            .Should().HaveLength(DirectMessageRules.SubjectMaxChars, "the limit is inclusive (COM-13)");

        var act = () => Start(new string('a', DirectMessageRules.SubjectMaxChars + 1));

        act.Should().Throw<ArgumentException>("a subject over the limit is refused (COM-13)");
    }

    [Fact]
    public void A_body_that_is_all_whitespace_is_refused()
    {
        var act = () => Write(" \n\t  \r\n ");

        act.Should().Throw<ArgumentException>("a body needs at least one character after trimming (COM-13)");
    }

    [Fact]
    public void A_body_over_the_limit_is_refused_and_one_at_the_limit_is_kept()
    {
        Write(new string('b', DirectMessageRules.BodyMaxChars)).Body
            .Should().HaveLength(DirectMessageRules.BodyMaxChars, "the limit is inclusive (COM-13)");

        var act = () => Write(new string('b', DirectMessageRules.BodyMaxChars + 1));

        act.Should().Throw<ArgumentException>("a body over the limit is refused (COM-13)");
    }

    [Fact]
    public void A_body_keeps_its_line_breaks_as_line_feeds()
    {
        var message = Write("First line\r\nSecond line\rThird line\nFourth");

        message.Body.Should().Be("First line\nSecond line\nThird line\nFourth", "a line break is kept (COM-13)");
    }

    [Fact]
    public void A_body_loses_control_characters_but_not_other_text()
    {
        var message = Write("Hel\u0000lo\u0007 <b>there</b> \u0085 😀");

        message.Body.Should().Be("Hello <b>there</b>  😀", "control characters are stripped and markup is stored as written (COM-13)");
    }

    [Fact]
    public void The_limit_is_counted_after_trimming_and_stripping()
    {
        var padded = "  " + new string('c', DirectMessageRules.BodyMaxChars) + "\u0000 \n";

        Write(padded).Body.Should().HaveLength(DirectMessageRules.BodyMaxChars, "the limit applies to what is stored (COM-13)");
    }

    [Fact]
    public void A_new_message_is_unread()
    {
        var message = Write();

        message.IsRead.Should().BeFalse("a message is unread until its recipient opens the conversation (COM-16)");
        message.ReadAt.Should().BeNull();
        message.SentAt.Should().Be(Now);
    }

    [Fact]
    public void Reading_marks_the_message_and_reading_twice_keeps_the_first_instant()
    {
        var message = Write();

        message.MarkRead(Now.AddMinutes(5));
        message.MarkRead(Now.AddMinutes(10));

        message.IsRead.Should().BeTrue();
        message.ReadAt.Should().Be(Now.AddMinutes(5), "reading twice does nothing (COM-16)");
    }

    [Fact]
    public void A_reply_moves_the_last_message_time_and_advances_the_version()
    {
        var conversation = Start();

        conversation.RecordReply(Now.AddHours(1));

        conversation.LastMessageAt.Should().Be(Now.AddHours(1), "a reply brings the thread to the top of the list");
        conversation.UpdatedAt.Should().Be(Now.AddHours(1));
        conversation.Version.Should().Be(2);
    }

    [Fact]
    public void A_reply_stamped_earlier_than_the_last_message_does_not_move_the_thread_back()
    {
        var conversation = Start();
        conversation.RecordReply(Now.AddHours(1));

        conversation.RecordReply(Now.AddMinutes(30));

        conversation.LastMessageAt.Should().Be(Now.AddHours(1), "the latest message time never goes backwards");
        conversation.Version.Should().Be(3);
    }

    [Fact]
    public void Only_the_two_managers_are_participants_and_each_sees_the_other()
    {
        var conversation = Start();

        conversation.IsParticipant(Alice).Should().BeTrue();
        conversation.IsParticipant(Ben).Should().BeTrue();
        conversation.IsParticipant(Guid.CreateVersion7()).Should().BeFalse("a third manager is not in the thread (COM-10)");
        conversation.OtherParticipant(Alice).Should().Be(Ben);
        conversation.OtherParticipant(Ben).Should().Be(Alice);

        var act = () => conversation.OtherParticipant(Guid.CreateVersion7());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_manager_cannot_block_themselves()
    {
        var act = () => ManagerBlock.Create(Alice, Alice, Now);

        act.Should().Throw<ArgumentException>("a block is between two managers (COM-15)");
    }

    [Fact]
    public void A_block_remembers_who_blocked_whom()
    {
        var block = ManagerBlock.Create(Alice, Ben, Now);

        block.BlockerManagerId.Should().Be(Alice);
        block.BlockedManagerId.Should().Be(Ben);
        block.CreatedAt.Should().Be(Now);
    }
}
