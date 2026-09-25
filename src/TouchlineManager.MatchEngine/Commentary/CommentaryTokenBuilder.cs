using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Commentary;

/// <summary>One named fact about an event, as a string that is safe to store and re-render.</summary>
/// <param name="Name">The parameter's name.</param>
/// <param name="Value">The parameter's value.</param>
public sealed record CommentaryParameter(string Name, string Value);

/// <summary>
/// One line of commentary: a template key, the facts it was filled from, and the English text.
/// </summary>
/// <remarks>
/// <para>
/// The template key and parameters are the durable part; the text is a rendering of them. Storing the key
/// rather than only the sentence is what makes the same match narratable in another language later without
/// re-simulating it, and what makes a wording change a presentation change rather than a historical one
/// (master plan §8.6).
/// </para>
/// <para>
/// Every parameter is a fact the manager can already see: a name, a club, a minute, a zone, a shirt number.
/// Hidden attributes never appear, because the builder is only ever handed events and the input's names —
/// there is no hidden value in its reach (`MAT-11`).
/// </para>
/// </remarks>
public sealed record CommentaryToken
{
    /// <summary>Gets the sequence number of the event this narrates.</summary>
    public required int EventSequence { get; init; }

    /// <summary>Gets the match minute.</summary>
    public required int Minute { get; init; }

    /// <summary>Gets the stoppage minute, or zero in regulation.</summary>
    public required int StoppageMinute { get; init; }

    /// <summary>Gets which side the event belongs to.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets the stable template key, which is what a translation keys off.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Gets which variant of the template was used, so repeated lines can be told apart.</summary>
    public required string VariantKey { get; init; }

    /// <summary>Gets the facts the line was built from.</summary>
    public required IReadOnlyList<CommentaryParameter> Parameters { get; init; }

    /// <summary>Gets the current English rendering.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// Turns a simulated match into commentary tokens (master plan §8.6, `MAT-8`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the input and the result. It reads the event stream and never influences it, which is
/// what `MAT-8` requires of every downstream consumer: commentary cannot change an outcome, and a change to a
/// sentence cannot change a result.
/// </para>
/// <para>
/// Repetition is avoided deterministically: each template has several variants and the event's sequence
/// number chooses between them, so the same match always produces the same words and a long match does not
/// read as one sentence repeated. A random variant would make the commentary unreproducible while the
/// result stayed reproducible, which is a confusing thing to have to explain.
/// </para>
/// </remarks>
public static class CommentaryTokenBuilder
{
    /// <summary>The version label of this template set.</summary>
    public const string Version = "commentary-v1";

    /// <summary>Builds the commentary for a finished match, in event order.</summary>
    /// <param name="input">The frozen snapshot, which supplies the names.</param>
    /// <param name="result">The simulated result.</param>
    /// <returns>One token per narrated event.</returns>
    public static IReadOnlyList<CommentaryToken> Build(MatchInputV1 input, MatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);

        var names = BuildNameLookup(input);
        var tokens = new List<CommentaryToken>();

        foreach (var matchEvent in result.Events.OrderBy(matchEvent => matchEvent.Sequence))
        {
            if (!Templates.TryGetValue(matchEvent.Type, out var template))
            {
                continue;
            }

            var facts = Facts(input, matchEvent, names);
            var variant = matchEvent.Sequence % template.Variants.Count;
            var text = Render(template.Variants[variant], facts);

            tokens.Add(new CommentaryToken
            {
                EventSequence = matchEvent.Sequence,
                Minute = matchEvent.Minute,
                StoppageMinute = matchEvent.StoppageMinute,
                Side = matchEvent.Side,
                TemplateKey = template.Key,
                VariantKey = $"{template.Key}.v{variant + 1}",
                Parameters =
                [
                    .. facts
                        .Where(fact => fact.Key != "clock" && fact.Key != "player" && fact.Key != "opponent")
                        .OrderBy(fact => fact.Key, StringComparer.Ordinal)
                        .Select(fact => new CommentaryParameter(fact.Key, fact.Value)),
                ],
                Text = text,
            });
        }

        return tokens;
    }

    private static Dictionary<string, string> Facts(
        MatchInputV1 input,
        EngineEventV1 matchEvent,
        IReadOnlyDictionary<Guid, string> names)
    {
        var side = input.SideOf(matchEvent.Side);
        var opponent = input.SideOf(MatchInputV1.OpponentOf(matchEvent.Side));

        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["club"] = side.ClubName,
            ["clubId"] = matchEvent.ClubId.ToString("D"),
            ["opponent"] = opponent.ClubName,
            ["clock"] = Clock(matchEvent),
        };

        if (matchEvent.ParticipantId is Guid player)
        {
            facts["player"] = NameOf(names, player);
            facts["playerId"] = player.ToString("D");
        }

        if (matchEvent.SecondaryParticipantId is Guid second)
        {
            facts["second"] = NameOf(names, second);
            facts["secondId"] = second.ToString("D");
        }

        if (matchEvent.Zone is ShotZone zone)
        {
            facts["shotZone"] = zone.Code();
        }

        if (matchEvent.AbsenceFixtures is int absence)
        {
            facts["absenceFixtures"] = absence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return facts;
    }

    /// <summary>Renders a template, substituting <c>{name}</c> placeholders and dropping unknown ones.</summary>
    private static string Render(string template, IReadOnlyDictionary<string, string> facts)
    {
        var rendered = template;

        foreach (var fact in facts)
        {
            rendered = rendered.Replace("{" + fact.Key + "}", fact.Value, StringComparison.Ordinal);
        }

        // A placeholder with no value would otherwise reach a manager as "{second}", so anything left is
        // replaced with a neutral phrase rather than left to read as a bug.
        var start = rendered.IndexOf('{', StringComparison.Ordinal);

        while (start >= 0)
        {
            var end = rendered.IndexOf('}', start);

            if (end < 0)
            {
                break;
            }

            rendered = string.Concat(rendered.AsSpan(0, start), "a team-mate", rendered.AsSpan(end + 1));
            start = rendered.IndexOf('{', StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string Clock(EngineEventV1 matchEvent) =>
        matchEvent.StoppageMinute > 0
            ? $"{matchEvent.Minute}+{matchEvent.StoppageMinute}"
            : matchEvent.Minute.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string NameOf(IReadOnlyDictionary<Guid, string> names, Guid participantId) =>
        names.TryGetValue(participantId, out var name) ? name : "a player";

    private static Dictionary<Guid, string> BuildNameLookup(MatchInputV1 input)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var side in new[] { input.Home, input.Away })
        {
            foreach (var participant in side.Squad)
            {
                names[participant.ParticipantId] = participant.DisplayName;
            }
        }

        return names;
    }

    private sealed record Template(string Key, IReadOnlyList<string> Variants);

    private static readonly Dictionary<EngineEventType, Template> Templates =
        new Dictionary<EngineEventType, Template>
        {
            [EngineEventType.KickOff] = new(
                "match.kickoff",
                [
                    "We are under way at {club}.",
                    "The whistle goes, and {club} get us started.",
                    "Kick-off, and the match is on.",
                ]),
            [EngineEventType.HalfTime] = new(
                "match.half_time",
                [
                    "That is the end of the first half.",
                    "Half-time.",
                    "The referee brings the first half to a close.",
                ]),
            [EngineEventType.SecondHalfStart] = new(
                "match.second_half",
                [
                    "Back under way for the second half.",
                    "The second half is under way.",
                    "Off we go again.",
                ]),
            [EngineEventType.FullTime] = new(
                "match.full_time",
                [
                    "That is full time.",
                    "The referee ends it.",
                    "Full time at {club}.",
                ]),
            [EngineEventType.Goal] = new(
                "match.goal",
                [
                    "Goal! {player} scores for {club}.",
                    "{player} finds the net for {club}.",
                    "It is in — {player} scores.",
                ]),
            [EngineEventType.PenaltyAwarded] = new(
                "match.penalty.awarded",
                [
                    "Penalty to {club}.",
                    "The referee points to the spot for {club}.",
                    "A penalty is given for {club}.",
                ]),
            [EngineEventType.PenaltyGoal] = new(
                "match.penalty.goal",
                [
                    "{player} converts the penalty.",
                    "It is a goal from the spot — {player} scores.",
                    "{player} scores from twelve yards.",
                ]),
            [EngineEventType.PenaltyMissed] = new(
                "match.penalty.missed",
                [
                    "{player} misses from the spot.",
                    "The penalty is not converted by {player}.",
                    "{player} cannot beat the goalkeeper from twelve yards.",
                ]),
            [EngineEventType.ShotSaved] = new(
                "match.shot.saved",
                [
                    "Saved! {player} is denied by the goalkeeper.",
                    "A fine save keeps out {player}.",
                    "The goalkeeper gets to {player}'s effort.",
                ]),
            [EngineEventType.ShotBlocked] = new(
                "match.shot.blocked",
                [
                    "{player}'s shot is blocked.",
                    "A block denies {player}.",
                    "The effort from {player} is charged down.",
                ]),
            [EngineEventType.ShotOffTarget] = new(
                "match.shot.off_target",
                [
                    "{player} puts it off target.",
                    "{player} drags the shot wide.",
                    "That is off target from {player}.",
                ]),
            [EngineEventType.Woodwork] = new(
                "match.shot.woodwork",
                [
                    "{player} hits the woodwork!",
                    "Off the frame of the goal from {player}.",
                    "{player} strikes the post or bar.",
                ]),
            [EngineEventType.Foul] = new(
                "match.foul",
                [
                    "A foul by {player}.",
                    "Free kick given against {player}.",
                    "{player} gives away a foul.",
                ]),
            [EngineEventType.YellowCard] = new(
                "match.card.yellow",
                [
                    "{player} is booked for {club}.",
                    "A yellow card for {player}.",
                    "{player} goes into the book.",
                ]),
            [EngineEventType.SecondYellowCard] = new(
                "match.card.second_yellow",
                [
                    "A second booking — {player} is off for {club}.",
                    "{player} is booked again and sent off.",
                    "{player} is dismissed after a second caution.",
                ]),
            [EngineEventType.RedCard] = new(
                "match.card.red",
                [
                    "{player} is sent off for {club}.",
                    "A red card for {player}.",
                    "{player} is dismissed.",
                ]),
            [EngineEventType.Offside] = new(
                "match.offside",
                [
                    "{player} is flagged offside.",
                    "Offside against {player}.",
                    "The flag goes up on {player}.",
                ]),
            [EngineEventType.Corner] = new(
                "match.corner",
                [
                    "Corner to {club}.",
                    "{club} win a corner.",
                    "It will be a corner for {club}.",
                ]),
            [EngineEventType.Injury] = new(
                "match.injury",
                [
                    "{player} is hurt and will not continue.",
                    "{player} requires treatment and comes off.",
                    "An injury to {player}.",
                ]),
            [EngineEventType.Substitution] = new(
                "match.substitution",
                [
                    "{second} replaces {player} for {club}.",
                    "A change for {club}: {second} comes on for {player}.",
                    "{club} bring on {second} in place of {player}.",
                ]),
        };
}
