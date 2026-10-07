using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>The one pace the whole film is played at, and what it was solved from (`replay-v4`).</summary>
/// <param name="Pace">How many times faster than real time every move is played.</param>
/// <param name="HoldScale">The factor the holds were shortened or lengthened by to fit the target.</param>
/// <param name="TargetSeconds">The film length aimed at, in seconds.</param>
/// <param name="MotionSeconds">The real-time length of all the moves, holds excluded.</param>
/// <param name="HoldSeconds">The film length of all the holds, after scaling.</param>
/// <param name="Condensed">How many possessions had their ground moves merged to fit.</param>
internal sealed record FilmPace(
    double Pace,
    double HoldScale,
    double TargetSeconds,
    double MotionSeconds,
    double HoldSeconds,
    int Condensed);

/// <summary>
/// Gives every beat a length, and solves the one pace the whole film is played at (`replay-v4`).
/// </summary>
/// <remarks>
/// <para>
/// Each move has a natural duration — its distance at the speed a ball of that kind travels, plus the time to
/// control it (a player already driving the ball has nothing to control) — and the dead-ball holds have fixed film
/// durations. The pace is the single number that fits all the
/// moves into what is left of the film once the holds are paid for: <c>p = motion / (target − holds)</c>. A viewer
/// therefore sees one speed from the first whistle to the last, instead of the time warp that made some passages
/// race and others crawl.
/// </para>
/// <para>
/// The pace is held inside a band. When quiet play would push it past the band the quietest possessions are
/// condensed first — consecutive ground moves by one side become one — and only then is the pace allowed higher,
/// and the holds shortened. When the film would be short of its target the pace stops at the band's floor and the
/// holds are lengthened instead. Nothing here ever lets the film run past its ceiling.
/// </para>
/// </remarks>
internal static class FilmTiming
{
    /// <summary>The shortest a move can take, in seconds, however short it is.</summary>
    private const double MinBeatSeconds = 0.25;

    /// <summary>How far inside the band's edge condensing aims, in pace.</summary>
    private const double PaceHeadroom = 0.06;

    /// <summary>How long a header takes, in seconds.</summary>
    private const double HeaderSeconds = 0.5;

    /// <summary>How long the goalkeeper takes to hold a save, in seconds.</summary>
    private const double SaveSeconds = 0.5;

    /// <summary>
    /// How long a ball held at the goal line is held for, in seconds of real time (`replay-v11`): about a second of film
    /// at the pace the film is played at, which is as long as the defenders take to close the man down.
    /// </summary>
    private const double ContestSeconds = 3.0;

    /// <summary>Gets the real-time length of a move.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="beat">The move.</param>
    /// <param name="distance">The distance the ball actually has to travel, when it is not the planned one.</param>
    public static double NaturalSeconds(FilmContext context, FilmBeat beat, double? distance = null)
    {
        var options = context.Options;
        var length = distance ?? beat.Distance;
        var control = options.ControlSeconds;

        var seconds = beat.Kind switch
        {
            BeatKind.Carry => length / CarrySpeed(context, beat),
            BeatKind.Pass => (length / options.PassMetresPerSecond) + control,
            BeatKind.LoftedPass => (length / options.LoftedPassMetresPerSecond) + control,
            BeatKind.Cross => (length / (beat.CornerKick ? options.CornerMetresPerSecond : options.CrossMetresPerSecond)) + control,
            BeatKind.Clearance => (length / options.ClearanceMetresPerSecond) + control,
            BeatKind.Shot => (length / (beat.Headed ? options.HeaderMetresPerSecond : options.ShotMetresPerSecond)) + control,
            BeatKind.Header => HeaderSeconds,
            BeatKind.Duel when beat.Contested => ContestSeconds,
            BeatKind.Duel => Math.Max(0.8, (length / options.DuelMetresPerSecond) + 0.5),
            BeatKind.Save => SaveSeconds,
            BeatKind.Placement => (length / options.PlacementMetresPerSecond) + 0.2,
            _ => 0.0,
        };

        return beat.IsHold ? 0.0 : Math.Max(MinBeatSeconds, seconds);
    }

    /// <summary>Gives every move its natural length.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="beats">The beats.</param>
    public static void Assign(FilmContext context, IEnumerable<FilmBeat> beats)
    {
        foreach (var beat in beats)
        {
            beat.NaturalSeconds = NaturalSeconds(context, beat);
        }
    }

    /// <summary>Gets the film length aimed at: nine to one on the played seconds, held between the floor and the ceiling.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="possessions">The recorded possessions.</param>
    public static double TargetSeconds(FilmContext context, IReadOnlyList<MatchPassageV1> possessions)
    {
        var options = context.Options;
        long played = 0;

        foreach (var possession in possessions)
        {
            played += Math.Max(0, possession.EndClockSeconds - possession.StartClockSeconds);
        }

        var seconds = (double)played / Math.Max(1, options.FilmMatchSecondsPerFilmSecond);

        return double.Clamp(seconds, options.MinFilmMilliseconds / 1_000.0, options.MaxFilmMilliseconds / 1_000.0);
    }

    /// <summary>Solves the one pace the film is played at.</summary>
    /// <param name="context">The film's context.</param>
    /// <param name="beats">The beats, with their natural lengths.</param>
    /// <param name="target">The film length aimed at, in seconds.</param>
    /// <param name="allowance">The share of extra time the motion is expected to need to meet its constraints.</param>
    /// <param name="condensed">How many possessions have already been condensed.</param>
    public static FilmPace Solve(FilmContext context, IReadOnlyList<FilmBeat> beats, double target, double allowance, int condensed)
    {
        var options = context.Options;
        var motion = beats.Where(beat => !beat.IsHold).Sum(beat => beat.NaturalSeconds) * (1.0 + allowance);
        var holds = beats.Where(beat => beat.IsHold).Sum(beat => beat.HoldFilmSeconds);

        return SolveFor(options, motion, holds, target, condensed);
    }

    /// <summary>Solves the pace for a motion length that is already known.</summary>
    public static FilmPace SolveFor(HighlightOptionsV1 options, double motion, double holds, double target, int condensed)
    {
        var min = options.MinPaceMilli / 1_000.0;
        var max = options.MaxPaceMilli / 1_000.0;
        var ceiling = options.CeilingPaceMilli / 1_000.0;
        var scale = 1.0;

        var room = Math.Max(0.5, target - holds);
        var pace = motion / room;

        if (pace < min)
        {
            // The film would be short of its target: stop at the floor and let the holds take up the slack.
            pace = min;

            var spare = target - ((motion / pace) + holds);

            if (spare > 0 && holds > 0)
            {
                scale = double.Min(options.MaxHoldGrowth, 1.0 + (spare / holds));
            }
        }
        else if (pace > ceiling)
        {
            // Past the ceiling pace the holds give way before the pace is allowed any higher.
            var affordable = holds > 0 ? (target - (motion / ceiling)) / holds : 1.0;

            scale = double.Clamp(affordable, options.MinHoldShare, 1.0);

            pace = motion / Math.Max(0.5, target - (holds * scale));
        }
        else if (pace > max)
        {
            // Inside the ceiling, above the band: allowed, once condensing has had its turn.
            scale = 1.0;
        }

        return new FilmPace(pace, scale, target, motion, holds * scale, condensed);
    }

    /// <summary>
    /// Picks the quietest possessions and merges their ground moves until the pace would fit the band.
    /// </summary>
    /// <param name="context">The film's context.</param>
    /// <param name="script">The script, which is rebuilt in place.</param>
    /// <param name="target">The film length aimed at, in seconds.</param>
    /// <param name="allowance">The share of extra time the motion is expected to need.</param>
    /// <returns>How many possessions were condensed.</returns>
    public static int Condense(FilmContext context, ref FilmScriptResult script, double target, double allowance)
    {
        var options = context.Options;
        var max = options.CondensePaceMilli / 1_000.0;

        var current = Solve(context, script.Beats, target, allowance, 0);

        if (current.Pace <= max)
        {
            return 0;
        }

        // Condensing aims a little inside the band, because how much the moves will have to be lengthened is an
        // estimate and the pace should not land just outside it.
        max -= PaceHeadroom;

        var candidates = QuietPossessions(context, script);
        var chosen = new Dictionary<int, List<FilmBeat>>();
        var motion = current.MotionSeconds;
        var holds = current.HoldSeconds;

        foreach (var possession in candidates)
        {
            var merged = Merge(context, script.Beats, possession);

            if (merged is null)
            {
                continue;
            }

            var saved = script.Beats
                .Skip(possession.FirstBeat)
                .Take(possession.LastBeat - possession.FirstBeat + 1)
                .Where(beat => !beat.IsHold)
                .Sum(beat => beat.NaturalSeconds)
                - merged.Where(beat => !beat.IsHold).Sum(beat => beat.NaturalSeconds);

            if (saved <= 0)
            {
                continue;
            }

            chosen[possession.Index] = merged;
            motion -= saved * (1.0 + allowance);

            if (motion / Math.Max(0.5, target - holds) <= max)
            {
                break;
            }
        }

        if (chosen.Count == 0)
        {
            return 0;
        }

        script = Rebuild(script, chosen);

        return chosen.Count;
    }

    /// <summary>
    /// Lists the possessions that can be condensed, the quietest first: those that end outside the final third, and
    /// then the rest of those with no event in them. A possession with an event, and the two before one, are never
    /// condensed: that is the build-up a manager came to see.
    /// </summary>
    private static List<FilmPossession> QuietPossessions(FilmContext context, FilmScriptResult script)
    {
        var final = context.Rules.ShotFinalThirdXMinBasisPoints - 1_600;
        var protectedFromCondensing = new HashSet<int>();

        foreach (var possession in script.Possessions)
        {
            if (possession.Source.Events.Any(recorded => IsChance(context, recorded.Sequence)))
            {
                protectedFromCondensing.Add(possession.Index);
                protectedFromCondensing.Add(possession.Index - 1);
                protectedFromCondensing.Add(possession.Index - 2);
            }
        }

        return
        [
            .. script.Possessions
                .Where(possession => possession.Source.Events.Count == 0 && !protectedFromCondensing.Contains(possession.Index))
                .OrderBy(possession => possession.Source.AttackingEndX < final ? 0 : 1)
                .ThenBy(possession => possession.Source.AttackingEndX)
                .ThenBy(possession => possession.Index),
        ];
    }

    private static bool IsChance(FilmContext context, int sequence) =>
        context.EventsBySequence.TryGetValue(sequence, out var matchEvent)
        && matchEvent.Type is EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
            or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget
            or EngineEventType.Woodwork or EngineEventType.FreeKickShot or EngineEventType.PenaltyAwarded;

    /// <summary>Merges the runs of consecutive ground moves by one side in a possession, or returns null when there are none.</summary>
    private static List<FilmBeat>? Merge(FilmContext context, List<FilmBeat> beats, FilmPossession possession)
    {
        var merged = new List<FilmBeat>();
        var changed = false;
        var run = new List<FilmBeat>();

        void Flush()
        {
            if (run.Count >= 2)
            {
                merged.Add(Combine(context, run));
                changed = true;
            }
            else
            {
                merged.AddRange(run);
            }

            run.Clear();
        }

        for (var index = possession.FirstBeat; index <= possession.LastBeat; index++)
        {
            var beat = beats[index];

            // A run does not come back to the player who began it: that would be a pass to himself (`replay-v10`).
            var returns = run.Count > 0 && !beat.ReceiverIsActor && beat.Receiver is not null && beat.Receiver == run[0].Actor;

            if (returns)
            {
                Flush();
            }

            if (beat.IsGroundMove && !beat.Cut && (run.Count == 0 || run[^1].Side == beat.Side))
            {
                run.Add(beat);

                continue;
            }

            Flush();

            if (beat.IsGroundMove && !beat.Cut)
            {
                run.Add(beat);
            }
            else
            {
                merged.Add(beat);
            }
        }

        Flush();

        return changed ? merged : null;
    }

    /// <summary>Combines a run of ground moves into one: the first player to the last, in a single pass.</summary>
    private static FilmBeat Combine(FilmContext context, List<FilmBeat> run)
    {
        var first = run[0];
        var last = run[^1];
        var distance = first.From.DistanceTo(last.To);

        // The ball ends with whoever the last move ended with. When that was a player driving it who is not the one
        // who starts the run (a keeper's kick, then a team-mate's carry), he receives the ball; the run is not a pass to
        // the player who played it (`replay-v10`).
        var solo = last.ReceiverIsActor && first.Actor is not null && first.Actor == last.Actor;
        var driven = last.ReceiverIsActor && !solo;

        var beat = new FilmBeat
        {
            Kind = solo ? BeatKind.Carry : distance >= 30.0 ? BeatKind.LoftedPass : BeatKind.Pass,
            Possession = first.Possession,
            Period = first.Period,
            Side = first.Side,
            From = first.From,
            To = last.To,
            Actor = first.Actor,
            ActorSource = first.ActorSource,
            Receiver = driven ? last.Actor : last.Receiver,
            ReceiverPending = driven ? last.Actor is null : last.ReceiverPending,
            ReceiverIsActor = solo,
            Formation = first.Formation,
            ZArc = !solo && distance >= 30.0 ? Math.Min(50, 12 + (0.9 * distance)) : 0,
        };

        beat.NaturalSeconds = NaturalSeconds(context, beat);

        foreach (var matchEvent in run.SelectMany(member => member.Events))
        {
            beat.Events.Add(matchEvent);
        }

        return beat;
    }

    /// <summary>Rebuilds the script with the chosen possessions' beats replaced, and the pins linked afresh.</summary>
    private static FilmScriptResult Rebuild(FilmScriptResult script, Dictionary<int, List<FilmBeat>> chosen)
    {
        var beats = new List<FilmBeat>(script.Beats.Count);
        var possessions = new List<FilmPossession>(script.Possessions.Count);
        var placed = 0;

        for (var index = 0; index < script.Beats.Count; index++)
        {
            var owner = script.Possessions.FirstOrDefault(possession => possession.FirstBeat == index);

            if (owner is not null && chosen.TryGetValue(owner.Index, out var replacement))
            {
                var first = beats.Count;

                beats.AddRange(replacement);
                possessions.Add(new FilmPossession(owner.Index, owner.Source, first, beats.Count - 1));
                index = owner.LastBeat;
                placed++;

                continue;
            }

            if (owner is not null)
            {
                var first = beats.Count;

                for (var member = owner.FirstBeat; member <= owner.LastBeat; member++)
                {
                    beats.Add(script.Beats[member]);
                }

                possessions.Add(new FilmPossession(owner.Index, owner.Source, first, beats.Count - 1));
                index = owner.LastBeat;

                continue;
            }

            // A beat between possessions: a half-time card or a substitution.
            beats.Add(script.Beats[index]);
        }

        _ = placed;

        return new FilmScriptResult(beats, possessions);
    }

    private static double CarrySpeed(FilmContext context, FilmBeat beat)
    {
        var options = context.Options;
        var dribbling = beat.Actor is Guid actor ? context.AttributeOf(actor, MatchAttributeName.Dribbling) : 10;
        var skill = double.Clamp((dribbling - 1) / 19.0, 0.0, 1.0);

        return options.CarryMetresPerSecondMin + ((options.CarryMetresPerSecondMax - options.CarryMetresPerSecondMin) * skill);
    }
}
