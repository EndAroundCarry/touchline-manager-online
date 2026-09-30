using FluentAssertions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Ops;

namespace TouchlineManager.Application.Tests.Ops;

/// <summary>
/// The opaque page cursors the operator's queue and audit reads carry (`F-46`, `F-47`, master plan §10).
/// </summary>
public sealed class AdminCursorTests
{
    [Fact]
    public void A_job_cursor_round_trips_and_refuses_a_value_it_did_not_produce()
    {
        var position = new AdminJobCursorPosition(
            new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var cursor = AdminJobCursor.Encode(position);

        AdminJobCursor.TryDecode(cursor, out var decoded).Should().BeTrue();
        decoded.Should().Be(position);

        AdminJobCursor.TryDecode(null, out var first).Should().BeTrue();
        first.Should().BeNull("no cursor is the first page, not an error");

        AdminJobCursor.TryDecode("not-a-cursor", out _).Should().BeFalse();
        AdminJobCursor.TryDecode(string.Empty, out _).Should().BeFalse("an empty cursor is not the first page");
    }

    [Fact]
    public void An_audit_cursor_round_trips_and_refuses_a_value_it_did_not_produce()
    {
        var position = new AdminAuditCursorPosition(
            new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var cursor = AdminAuditCursor.Encode(position);

        AdminAuditCursor.TryDecode(cursor, out var decoded).Should().BeTrue();
        decoded.Should().Be(position);

        AdminAuditCursor.TryDecode(null, out var first).Should().BeTrue();
        first.Should().BeNull("no cursor is the first page, not an error");

        AdminAuditCursor.TryDecode("not-a-cursor", out _).Should().BeFalse();
        AdminAuditCursor.TryDecode(string.Empty, out _).Should().BeFalse("an empty cursor is not the first page");
    }
}
