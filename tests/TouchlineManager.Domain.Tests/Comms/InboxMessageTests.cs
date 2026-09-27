using FluentAssertions;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Domain.Tests.Comms;

/// <summary>
/// An inbox message: what it records, and that reading it is the only mutation (`F-41`, master plan §6.9).
/// </summary>
public sealed class InboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private static InboxMessage Record() =>
        InboxMessage.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InboxCategory.Result,
            InboxTemplatesForTest.Template,
            """{"roundNumber":1}""",
            Guid.CreateVersion7(),
            Now);

    [Fact]
    public void A_new_message_is_unread_and_keeps_its_template()
    {
        var message = Record();

        message.IsRead.Should().BeFalse();
        message.ReadAt.Should().BeNull();
        message.Version.Should().Be(1);
        message.CreatedAt.Should().Be(Now);
        message.Category.Should().Be(InboxCategory.Result);
    }

    [Fact]
    public void Reading_marks_the_message_and_advances_the_version()
    {
        var message = Record();

        message.MarkRead(Now.AddMinutes(5));

        message.IsRead.Should().BeTrue();
        message.ReadAt.Should().Be(Now.AddMinutes(5));
        message.Version.Should().Be(2);
        message.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Reading_twice_is_a_no_op()
    {
        var message = Record();

        message.MarkRead(Now.AddMinutes(5));
        message.MarkRead(Now.AddMinutes(10));

        message.ReadAt.Should().Be(Now.AddMinutes(5), "the first read is when it was read");
        message.Version.Should().Be(2, "a retried read must not advance the version");
    }

    [Fact]
    public void A_message_needs_a_recipient_a_template_and_parameters()
    {
        var noRecipient = () => InboxMessage.Record(
            Guid.CreateVersion7(),
            Guid.Empty,
            InboxCategory.Result,
            InboxTemplatesForTest.Template,
            "{}",
            null,
            Now);

        var noTemplate = () => InboxMessage.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InboxCategory.Result,
            "  ",
            "{}",
            null,
            Now);

        var longTemplate = () => InboxMessage.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InboxCategory.Result,
            new string('x', InboxMessage.MaxTemplateKeyLength + 1),
            "{}",
            null,
            Now);

        var noParameters = () => InboxMessage.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            InboxCategory.Result,
            InboxTemplatesForTest.Template,
            string.Empty,
            null,
            Now);

        noRecipient.Should().Throw<ArgumentException>();
        noTemplate.Should().Throw<ArgumentException>();
        longTemplate.Should().Throw<ArgumentException>();
        noParameters.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_category_round_trips_through_its_code()
    {
        foreach (var category in InboxCategories.All)
        {
            InboxCategories.FromCode(category.ToCode()).Should().Be(category);
        }

        InboxCategories.All.Should().OnlyHaveUniqueItems(category => category.ToCode());
        InboxCategories.All.Should().OnlyContain(
            category => category.ToCode().Length <= InboxCategories.MaxCodeLength);
    }

    /// <summary>A template key the aggregate accepts, kept out of the way of the code under test.</summary>
    private static class InboxTemplatesForTest
    {
        /// <summary>A plausible key, so the length contract is exercised rather than a one-character stub.</summary>
        public const string Template = "inbox.result.recorded";
    }
}
