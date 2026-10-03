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
/// One line of commentary pinned to a moment inside a highlight (`replay-v2`, Stage 3).
/// </summary>
/// <remarks>
/// The full match log narrates by the minute; a replay needs better resolution than that, because a passage
/// of play is ten to twenty-five seconds long and its build-up, its strike, and its outcome all happen
/// inside the same minute. The offset is milliseconds from the passage's first frame, which is what lets the
/// bottom ticker overwrite in time with the action on the pitch. Keys and parameters are the durable part,
/// exactly as they are for <see cref="CommentaryToken"/>.
/// </remarks>
public sealed record HighlightCommentaryV1
{
    /// <summary>Gets the offset from the passage's first frame, in milliseconds.</summary>
    public required int TimeMilliseconds { get; init; }

    /// <summary>Gets the stable template key, which is what a translation keys off.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Gets which variant of the template was used, so repeated lines can be told apart.</summary>
    public required string VariantKey { get; init; }

    /// <summary>Gets the facts the line was built from.</summary>
    public required IReadOnlyList<CommentaryParameter> Parameters { get; init; }

    /// <summary>Gets the current English rendering.</summary>
    public required string Text { get; init; }

    /// <summary>Gets an estimate of the serialized payload, on the same accounting the highlights use.</summary>
    public int EstimatedPayloadBytes =>
        (Text.Length * 2) + Parameters.Sum(parameter => (parameter.Name.Length + parameter.Value.Length) * 2) + 48;
}

/// <summary>
/// What kind of build-up moment a film beat narrates (`commentary-v3`).
/// </summary>
/// <remarks>
/// The build-up families a continuous film needs, one per meaningful touch. A beat whose kind is
/// <see cref="Event"/> names the event it narrates and is rendered from the full log's own templates, so the
/// feed and the report never word the same goal differently.
/// </remarks>
public enum PassageBeatKind
{
    /// <summary>A progressive pass or a pass received.</summary>
    Pass = 0,

    /// <summary>A player carried the ball.</summary>
    Carry = 1,

    /// <summary>A player dribbled past a challenge.</summary>
    Dribble = 2,

    /// <summary>A ball crossed into the box.</summary>
    Cross = 3,

    /// <summary>A header won.</summary>
    Header = 4,

    /// <summary>A tackle made.</summary>
    Tackle = 5,

    /// <summary>The ball intercepted.</summary>
    Interception = 6,

    /// <summary>A save made.</summary>
    Save = 7,

    /// <summary>A shot, penalty, or free kick taken.</summary>
    Chance = 8,

    /// <summary>An event, rendered from the full match log's own template for its type.</summary>
    Event = 9,

    /// <summary>A goal kick is about to be taken (`commentary-v4`).</summary>
    GoalKick = 10,

    /// <summary>The goalkeeper has the ball and is about to play it (`commentary-v4`).</summary>
    KeeperBall = 11,

    /// <summary>A free kick is taken quickly, with no shot in it (`commentary-v4`).</summary>
    FreeKick = 12,
}

/// <summary>
/// One narrated moment inside a film passage (`commentary-v3`).
/// </summary>
/// <remarks>
/// The director decides which touches and events are worth narrating and where they sit on the passage's
/// film clock; the builder decides the words. Every fact here is one a manager can already see — a player, a
/// club, a minute — and never a hidden value (`MAT-11`).
/// </remarks>
/// <param name="TimeMilliseconds">The offset from the passage's first frame, in milliseconds.</param>
/// <param name="Side">Which side the beat belongs to.</param>
/// <param name="ParticipantId">The player involved, absent for a beat that names nobody.</param>
/// <param name="Kind">What kind of moment it is.</param>
/// <param name="Event">The event to narrate when <paramref name="Kind"/> is <see cref="PassageBeatKind.Event"/>.</param>
/// <param name="Seed">A stable seed that chooses the variant, so repeated lines can be told apart.</param>
public sealed record PassageBeatV1(
    int TimeMilliseconds,
    MatchSide Side,
    Guid? ParticipantId,
    PassageBeatKind Kind,
    EngineEventV1? Event,
    int Seed);

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
    public const string Version = "commentary-v4";

    /// <summary>The delay between a strike and the line that reports where it ended up.</summary>
    private const int OutcomeDelayMilliseconds = 900;

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

    /// <summary>
    /// Builds the synchronized commentary for one highlight passage (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// Three moments are worth narrating in a ten-to-twenty-five-second passage: the build-up as it starts,
    /// the strike when the shooter reaches the ball, and the outcome a beat later. The outcome reuses the
    /// match log's own template, so the ticker and the report never word the same goal differently. Times are
    /// clamped into the passage, so a caller can never hand a client a line that plays after the whistle.
    /// </remarks>
    /// <param name="input">The frozen snapshot, which supplies the names.</param>
    /// <param name="matchEvent">The event the highlight presents.</param>
    /// <param name="durationMilliseconds">How long the passage runs for.</param>
    /// <param name="strikeMilliseconds">When in the passage the ball is struck, or zero for a set piece.</param>
    public static IReadOnlyList<HighlightCommentaryV1> BuildPassage(
        MatchInputV1 input,
        EngineEventV1 matchEvent,
        int durationMilliseconds,
        int strikeMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(matchEvent);

        if (durationMilliseconds <= 0)
        {
            return [];
        }

        var names = BuildNameLookup(input);
        var facts = Facts(input, matchEvent, names);
        var lines = new List<HighlightCommentaryV1>
        {
            Token(0, PassageOpener(matchEvent.Type), matchEvent.Sequence, facts),
        };

        var actionAt = int.Clamp(strikeMilliseconds, 1, durationMilliseconds);
        lines.Add(Token(actionAt, PassageActionTemplate(matchEvent.Type), matchEvent.Sequence + 1, facts));

        var outcome = OutcomeTemplate(matchEvent.Type);

        if (outcome is not null)
        {
            var outcomeAt = Math.Min(durationMilliseconds, actionAt + OutcomeDelayMilliseconds);
            lines.Add(Token(outcomeAt, outcome, matchEvent.Sequence + 2, facts));
        }

        return lines;
    }

    /// <summary>
    /// Maps a recorded passage action to the build-up family that narrates it, or null for one not narrated
    /// (`commentary-v3`).
    /// </summary>
    /// <param name="action">What the player did with the ball.</param>
    public static PassageBeatKind? KindOf(PassageAction action) => action switch
    {
        PassageAction.Pass or PassageAction.Receive => PassageBeatKind.Pass,
        PassageAction.Carry => PassageBeatKind.Carry,
        PassageAction.Cross => PassageBeatKind.Cross,
        PassageAction.Header => PassageBeatKind.Header,
        PassageAction.Tackle => PassageBeatKind.Tackle,
        PassageAction.Interception => PassageBeatKind.Interception,
        PassageAction.Save or PassageAction.Dive => PassageBeatKind.Save,
        PassageAction.Shot or PassageAction.Penalty or PassageAction.FreeKick => PassageBeatKind.Chance,
        _ => null,
    };

    /// <summary>
    /// Builds the synchronized commentary for a film passage from its beats (`replay-v3`, `commentary-v3`).
    /// </summary>
    /// <remarks>
    /// The director hands over the touches and events it decided are worth narrating, already on the passage's
    /// own film clock; the builder turns each into a line. A build-up beat uses its family's template, and an
    /// event beat reuses the full log's template for its type, so a goal reads the same in the feed as it does
    /// in the report. Times are clamped into the passage, so a caller can never hand a client a line that
    /// plays after the whistle.
    /// </remarks>
    /// <param name="input">The frozen snapshot, which supplies the names.</param>
    /// <param name="beats">The passage's narrated moments, ordered by the director.</param>
    /// <param name="durationMilliseconds">How long the passage runs for in the film.</param>
    public static IReadOnlyList<HighlightCommentaryV1> BuildPassageCommentary(
        MatchInputV1 input,
        IReadOnlyList<PassageBeatV1> beats,
        int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(beats);

        if (durationMilliseconds <= 0 || beats.Count == 0)
        {
            return [];
        }

        var names = BuildNameLookup(input);
        var lines = new List<HighlightCommentaryV1>(beats.Count);

        foreach (var beat in beats.OrderBy(beat => beat.TimeMilliseconds))
        {
            var template = TemplateFor(beat);

            if (template is null)
            {
                continue;
            }

            var facts = BeatFacts(input, names, beat);
            var variant = ((beat.Seed % template.Variants.Count) + template.Variants.Count) % template.Variants.Count;

            lines.Add(new HighlightCommentaryV1
            {
                TimeMilliseconds = int.Clamp(beat.TimeMilliseconds, 0, durationMilliseconds),
                TemplateKey = template.Key,
                VariantKey = $"{template.Key}.v{variant + 1}",
                Parameters = DurableParameters(facts),
                Text = Render(template.Variants[variant], facts),
            });
        }

        return lines;
    }

    /// <summary>The template a beat renders from: its own family, or the event's full-log template.</summary>
    private static Template? TemplateFor(PassageBeatV1 beat) => beat.Kind switch
    {
        PassageBeatKind.Event => beat.Event is { } matchEvent && Templates.TryGetValue(matchEvent.Type, out var template)
            ? template
            : null,
        PassageBeatKind.Pass => BuildTemplates["pass"],
        PassageBeatKind.Carry => BuildTemplates["carry"],
        PassageBeatKind.Dribble => BuildTemplates["dribble"],
        PassageBeatKind.Cross => BuildTemplates["cross"],
        PassageBeatKind.Header => BuildTemplates["header"],
        PassageBeatKind.Tackle => BuildTemplates["tackle"],
        PassageBeatKind.Interception => BuildTemplates["interception"],
        PassageBeatKind.Save => BuildTemplates["save"],
        PassageBeatKind.Chance => BuildTemplates["chance"],
        PassageBeatKind.GoalKick => BuildTemplates["goal_kick"],
        PassageBeatKind.KeeperBall => BuildTemplates["keeper_ball"],
        PassageBeatKind.FreeKick => BuildTemplates["free_kick"],
        _ => null,
    };

    /// <summary>The facts a beat is rendered from: the event's own, or a player-fact set for a build-up beat.</summary>
    private static Dictionary<string, string> BeatFacts(
        MatchInputV1 input,
        IReadOnlyDictionary<Guid, string> names,
        PassageBeatV1 beat)
    {
        if (beat.Kind == PassageBeatKind.Event && beat.Event is { } matchEvent)
        {
            return Facts(input, matchEvent, names);
        }

        var side = input.SideOf(beat.Side);
        var opponent = input.SideOf(MatchInputV1.OpponentOf(beat.Side));

        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["club"] = side.ClubName,
            ["clubId"] = side.ClubId.ToString("D"),
            ["opponent"] = opponent.ClubName,
        };

        if (beat.ParticipantId is Guid player)
        {
            facts["player"] = NameOf(names, player);
            facts["playerId"] = player.ToString("D");
        }

        return facts;
    }

    /// <summary>The durable parameters of a line, with the rendering-only facts dropped.</summary>
    private static IReadOnlyList<CommentaryParameter> DurableParameters(IReadOnlyDictionary<string, string> facts) =>
    [
        .. facts
            .Where(fact => fact.Key != "clock" && fact.Key != "player" && fact.Key != "opponent")
            .OrderBy(fact => fact.Key, StringComparer.Ordinal)
            .Select(fact => new CommentaryParameter(fact.Key, fact.Value)),
    ];

    private static HighlightCommentaryV1 Token(
        int timeMilliseconds,
        Template template,
        int variantSeed,
        Dictionary<string, string> facts)
    {
        var variant = ((variantSeed % template.Variants.Count) + template.Variants.Count) % template.Variants.Count;

        return new HighlightCommentaryV1
        {
            TimeMilliseconds = timeMilliseconds,
            TemplateKey = template.Key,
            VariantKey = $"{template.Key}.v{variant + 1}",
            Parameters =
            [
                .. facts
                    .Where(fact => fact.Key != "clock" && fact.Key != "player" && fact.Key != "opponent")
                    .OrderBy(fact => fact.Key, StringComparer.Ordinal)
                    .Select(fact => new CommentaryParameter(fact.Key, fact.Value)),
            ],
            Text = Render(template.Variants[variant], facts),
        };
    }

    private static Template PassageOpener(EngineEventType type) =>
        type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed or EngineEventType.FreeKickShot
            ? PassageTemplates["set_piece"]
            : PassageTemplates["build_up"];

    private static Template PassageActionTemplate(EngineEventType type) => type switch
    {
        EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => PassageTemplates["penalty"],
        EngineEventType.FreeKickShot => PassageTemplates["free_kick"],
        _ => PassageTemplates["shot"],
    };

    private static Template? OutcomeTemplate(EngineEventType type) =>
        Templates.TryGetValue(type, out var template) ? template : null;

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

    /// <summary>The passage templates, keyed by the moment they narrate rather than by event type.</summary>
    private static readonly Dictionary<string, Template> PassageTemplates = new(StringComparer.Ordinal)
    {
        ["build_up"] = new(
            "match.passage.build_up",
            [
                "{club} work the opening.",
                "{club} build the move.",
                "The ball is moved forward by {club}.",
            ]),
        ["set_piece"] = new(
            "match.passage.set_piece",
            [
                "{club} set themselves for the set piece.",
                "The set piece is set up for {club}.",
                "{club} line up the dead ball.",
            ]),
        ["shot"] = new(
            "match.passage.shot",
            [
                "{player} lets fly.",
                "{player} goes for goal.",
                "The chance falls to {player}.",
            ]),
        ["penalty"] = new(
            "match.passage.penalty",
            [
                "{player} steps up from twelve yards.",
                "{player} places the ball on the spot.",
                "It is {player} who takes the penalty.",
            ]),
        ["free_kick"] = new(
            "match.passage.free_kick",
            [
                "{player} strikes the free kick.",
                "{player} goes directly for goal.",
                "The free kick is {player}'s to take.",
            ]),
    };

    /// <summary>
    /// The build-up families a continuous film narrates from (`commentary-v3`), keyed by the moment they name.
    /// </summary>
    /// <remarks>
    /// Every meaningful touch gets a family rather than a fixed opening line, which is what turns the feed from
    /// three generic sentences a passage into a readable account of the move. Filler square passes are simply
    /// not handed to the builder, so the policy of skipping them lives in the director's beat selection rather
    /// than in a template.
    /// </remarks>
    private static readonly Dictionary<string, Template> BuildTemplates = new(StringComparer.Ordinal)
    {
        ["pass"] = new(
            "match.build.pass",
            [
                "{player} plays it forward.",
                "{player} finds a team-mate.",
                "{player} moves it on.",
                "{player} keeps the move going.",
            ]),
        ["carry"] = new(
            "match.build.carry",
            [
                "{player} carries the ball forward.",
                "{player} drives on.",
                "{player} advances with it.",
                "{player} strides forward.",
            ]),
        ["dribble"] = new(
            "match.build.dribble",
            [
                "{player} beats his man.",
                "{player} skips past a challenge.",
                "{player} goes past one.",
                "{player} dribbles through.",
            ]),
        ["cross"] = new(
            "match.build.cross",
            [
                "{player} swings in a cross.",
                "{player} delivers into the box.",
                "{player} whips it in.",
                "{player} sends a cross over.",
            ]),
        ["header"] = new(
            "match.build.header",
            [
                "{player} rises to head it.",
                "{player} meets it with his head.",
                "{player} wins the header.",
                "{player} heads it on.",
            ]),
        ["tackle"] = new(
            "match.build.tackle",
            [
                "{player} makes the tackle.",
                "{player} wins it back.",
                "{player} slides in and takes it.",
                "{player} dispossesses his man.",
            ]),
        ["interception"] = new(
            "match.build.interception",
            [
                "{player} intercepts.",
                "{player} reads it and cuts it out.",
                "{player} steps in front.",
                "{player} nips in to intercept.",
            ]),
        ["save"] = new(
            "match.build.save",
            [
                "{player} saves it.",
                "{player} gets a hand to it.",
                "{player} keeps it out.",
                "A save by {player}.",
            ]),
        ["chance"] = new(
            "match.build.chance",
            [
                "{player} has a go.",
                "{player} lets fly.",
                "The chance falls to {player}.",
                "{player} goes for goal.",
            ]),

        // The restarts a continuous film shows (`commentary-v4`).
        ["goal_kick"] = new(
            "match.restart.goal_kick",
            [
                "{player} takes the goal kick for {club}.",
                "Goal kick, and {player} sends it long for {club}.",
                "{player} restarts for {club} from the goal kick.",
            ]),
        ["keeper_ball"] = new(
            "match.restart.keeper_ball",
            [
                "{player} has it, and {club} start again from the back.",
                "{player} collects it and rolls it out for {club}.",
                "The keeper, {player}, gets {club} going again.",
            ]),
        ["free_kick"] = new(
            "match.restart.free_kick",
            [
                "{player} takes the free kick quickly for {club}.",
                "A free kick to {club}, and {player} wastes no time.",
                "{player} plays the free kick on.",
            ]),
    };

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
            [EngineEventType.FreeKickWon] = new(
                "match.free_kick.won",
                [
                    "A free kick to {club} in a dangerous area.",
                    "{player} is fouled — free kick to {club}.",
                    "Free kick for {club}, within shooting range.",
                ]),
            [EngineEventType.FreeKickShot] = new(
                "match.free_kick.struck",
                [
                    "{player} lines it up and strikes the free kick.",
                    "The free kick is taken by {player}.",
                    "{player} goes for goal from the free kick.",
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
