using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// Finds where a move began: the frame a film should open on so that a chance is seen with the play that made it
/// (`tick-film-v1`, Milestone 2).
/// </summary>
/// <remarks>
/// <para>
/// A chance is the end of a move, and the move began when the side last won the ball: by a tackle, an interception, a keeper's claim or a
/// restart. The film used to show a fixed fifteen or eighteen seconds before a chance whatever the move was, so a long build-up lost its
/// first half and a short one showed the play before it. The finder walks back from the strike, frame by frame, for as long as the side
/// keeps the ball.
/// </para>
/// <para>
/// Who has the ball in a frame is the side of the man who controls it; while it is loose, the side of whoever played it last, so a pass in
/// flight is still its passer's. A tackle, an interception and a keeper's claim all leave the other side's man in control, which is how the
/// walk sees a regain without being told which action it was. Two gaps are forgiven because they are not regains: a ball nobody holds for
/// up to <see cref="LooseGap"/> frames (a poke loose, a block, a flick-on that comes back), and an opponent's touch of up to
/// <see cref="TouchGap"/> frames (a deflection that never becomes control). Play stopping for a restart ends the walk too, and
/// the move is then the restart's kick and what followed.
/// </para>
/// <para>
/// The answer is a pure function of the recording, so a replay is the same on every machine.
/// </para>
/// </remarks>
internal static class TickMoveFinder
{
    /// <summary>The play shown before the regain, in frames (3 s): the turnover and the man who lost the ball are on screen.</summary>
    public const int LeadIn = 30;

    /// <summary>The play shown before a restart's kick, in frames (1 s): there is no turnover to show, only the ball being put down.</summary>
    public const int RestartLeadIn = 10;

    /// <summary>The shortest a move's lead is unless the caller asks for more, in frames (10 s).</summary>
    public const int MinLead = 100;

    /// <summary>The longest a move's lead is, in frames (40 s), before it is snapped back to a pass.</summary>
    public const int MaxLead = 400;

    /// <summary>How far back, in frames (10 s), a capped lead looks for a pass to open on.</summary>
    public const int SnapReach = 100;

    /// <summary>The longest a ball nobody holds is forgiven, in frames (1.5 s).</summary>
    public const int LooseGap = 15;

    /// <summary>The longest an opponent's control is forgiven, in frames (0.5 s).</summary>
    public const int TouchGap = 5;

    /// <summary>How far back a strike is looked for before an event, in frames (8 s).</summary>
    private const int StrikeReach = 80;

    private const int Half = TickMatchRecording.Entities / 2;

    /// <summary>Finds the frame a move should be shown from.</summary>
    /// <param name="recording">The continuous trace.</param>
    /// <param name="eventFrame">The frame the chance's event was emitted at.</param>
    /// <param name="side">The side that made the chance: 0 for the home side, 1 for the away side.</param>
    /// <param name="minLead">The least lead to return, in frames, however short the move was.</param>
    /// <returns>
    /// The first frame to show: at least <paramref name="minLead"/> and at most <see cref="MaxLead"/> (a little more when it was snapped back
    /// to a pass) before <paramref name="eventFrame"/>, and never in the half before. A lead that reaches back over a restart shows it:
    /// the film fades over the jump, as it does anywhere the ball is put down.
    /// </returns>
    public static int StartOf(TickMatchRecording recording, int eventFrame, int side, int minLead = MinLead)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.FrameCount == 0)
        {
            return 0;
        }

        var frame = Math.Clamp(eventFrame, 0, recording.FrameCount - 1);
        var strike = StrikeOf(recording, side, frame);
        var floor = recording.SecondHalfFrame > 0 && frame >= recording.SecondHalfFrame ? recording.SecondHalfFrame : 0;
        var regain = RegainOf(recording, side, strike, floor, out var atRestart);
        var start = regain - (atRestart ? RestartLeadIn : LeadIn);

        if (frame - start > MaxLead)
        {
            start = SnapToPass(recording, side, frame - MaxLead);
        }

        if (frame - start < minLead)
        {
            start = frame - minLead;
        }

        return Math.Max(floor, start);
    }

    /// <summary>
    /// Finds the frame a side struck the shot an event is about: an event is emitted when the keeper or the net has dealt with the shot,
    /// a second or two after the kick. With no kick in open play (a goal off a deflection) it is the last frame of open play before the event.
    /// </summary>
    private static int StrikeOf(TickMatchRecording recording, int side, int frame)
    {
        var actions = recording.Actions;

        for (var index = LastAtOrBefore(actions, frame); index >= 0; index--)
        {
            var stamp = actions[index];

            if (frame - stamp.Frame > StrikeReach)
            {
                break;
            }

            // A goal is stamped a second time when the ball crosses the line, in the celebration: the strike is the kick in open play.
            if (stamp.Entity >= 0
                && stamp.Entity / Half == side
                && stamp.Action is PassageAction.Shot or PassageAction.FreeKick or PassageAction.Penalty
                && recording.State(Math.Min(stamp.Frame, recording.FrameCount - 1)) == TickPlayState.OpenPlay)
            {
                return stamp.Frame;
            }
        }

        while (frame > 0 && recording.State(frame) != TickPlayState.OpenPlay)
        {
            frame--;
        }

        return frame;
    }

    /// <summary>Walks back from a frame for as long as a side keeps the ball and returns the first frame of that spell.</summary>
    private static int RegainOf(TickMatchRecording recording, int side, int from, int floor, out bool atRestart)
    {
        var actions = recording.Actions;
        var action = LastAtOrBefore(actions, from);
        var start = from;
        var run = 0;
        var controlled = false;

        atRestart = false;

        for (var frame = from; frame >= floor; frame--)
        {
            if (recording.State(frame) != TickPlayState.OpenPlay)
            {
                atRestart = true;

                break;
            }

            var controller = recording.Controller(frame);
            var holder = controller >= 0 ? controller / Half : TouchSide(actions, ref action, frame);

            if (holder == side)
            {
                start = frame;
                run = 0;
                controlled = false;

                continue;
            }

            run++;
            controlled |= controller >= 0;

            if (run > (controlled ? TouchGap : LooseGap))
            {
                break;
            }
        }

        return start;
    }

    /// <summary>
    /// Finds the side of whoever played the ball last at a frame, walking <paramref name="index"/> back through the actions as the frame
    /// goes back. Running and celebrating are not touches of the ball, and neither is a stamp that blames a man for losing it.
    /// </summary>
    private static int TouchSide(List<TickActionStamp> actions, ref int index, int frame)
    {
        while (index >= 0 && (actions[index].Frame > frame || actions[index].Entity < 0 || actions[index].Action is PassageAction.Run or PassageAction.Celebrate || actions[index].Action.IsBlame()))
        {
            index--;
        }

        return index >= 0 ? actions[index].Entity / Half : -1;
    }

    /// <summary>Opens a capped move on a kick: the side's last pass at or before a frame, if one is near enough.</summary>
    private static int SnapToPass(TickMatchRecording recording, int side, int capped)
    {
        var actions = recording.Actions;

        for (var index = LastAtOrBefore(actions, capped); index >= 0; index--)
        {
            var stamp = actions[index];

            if (capped - stamp.Frame > SnapReach)
            {
                break;
            }

            if (stamp.Entity >= 0 && stamp.Entity / Half == side && stamp.Action == PassageAction.Pass)
            {
                return stamp.Frame;
            }
        }

        return capped;
    }

    /// <summary>Gets the index of the last action at or before a frame, or -1.</summary>
    private static int LastAtOrBefore(List<TickActionStamp> actions, int frame)
    {
        var low = 0;
        var high = actions.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);

            if (actions[middle].Frame <= frame)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}
