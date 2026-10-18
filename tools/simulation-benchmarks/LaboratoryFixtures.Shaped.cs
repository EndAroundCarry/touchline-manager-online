using System.Globalization;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// What it takes to move a position-shaped squad into another shape with every player in his natural slot (plan snoopy-toasting-hamster,
/// Milestone 1).
/// </summary>
/// <param name="Target">The shape the squad moves to.</param>
/// <param name="Substitutions">The named substitutions the move needs: bench players for slots no starter fits, for the starters left over.</param>
/// <param name="Natural">Every outfield player of the new eleven in his natural slot (the substitutes included), by new slot number.</param>
/// <param name="Mismatch">The same eleven placed as badly as the slots allow: out of their band wherever it can be done.</param>
/// <param name="SeatOf">The slot number each player stood in at kick-off; a substitute has his team-mate's.</param>
/// <param name="AutoFitOffNatural">How many of the ten outfield starters the engine's own fit leaves off-natural, with no substitutions made.</param>
/// <param name="Description">One line saying what the move is.</param>
internal sealed record ShapedMove(
    FormationPreset Target,
    IReadOnlyList<MatchRuleSubstitutionV1> Substitutions,
    IReadOnlyList<MatchSlotOverrideV1> Natural,
    IReadOnlyList<MatchSlotOverrideV1> Mismatch,
    IReadOnlyDictionary<Guid, int> SeatOf,
    int AutoFitOffNatural,
    string Description);

/// <summary>A laboratory side whose players are shaped by their position, with the moves it can make to the shapes it was built for.</summary>
/// <param name="ClubSeed">1 for the home side, 2 for the away side.</param>
/// <param name="Start">The shape the side starts in.</param>
/// <param name="Side">The side as the engine takes it.</param>
/// <param name="StarterMean">The mean of the 24 outfield attributes over the ten outfield starters, as reached.</param>
/// <param name="Moves">The moves to the shapes it was built for, by shape.</param>
/// <param name="Lines">The squad as text: who stands where, the bench, the mean reached.</param>
internal sealed record ShapedSquad(
    int ClubSeed,
    FormationPreset Start,
    MatchSideV1 Side,
    double StarterMean,
    IReadOnlyDictionary<FormationPreset, ShapedMove> Moves,
    IReadOnlyList<string> Lines);

internal static partial class LaboratoryFixtures
{
    /// <summary>The attributes an outfield player's ability is read from: the first 24 of the canonical order, without the goalkeeping four.</summary>
    private const int OutfieldAttributes = 24;

    /// <summary>The outfield players on a bench of seven that already holds a keeper.</summary>
    private const int BenchOutfield = 6;

    /// <summary>A cost no assignment may take.</summary>
    private const long Forbidden = long.MaxValue / 4;

    /// <summary>A slot left to a substitute costs more than any walk the starters could make, so as many starters as can be natural stay.</summary>
    private const long SlotForASubstitute = 1_000_000;

    /// <summary>How many times an unfamiliar grade outweighs a unit of walking, when the aim is the worst placement.</summary>
    private const long MismatchWeight = 1_000;

    /// <summary>The offsets tried when the squad's mean is brought back to its target, in hundredths of an attribute point either side.</summary>
    private const int OffsetSearchHundredths = 400;

    private static readonly AttributeName[] DomainAttributes =
        [.. MatchAttributeNames.All.Select(name => Enum.Parse<AttributeName>(name.ToString()))];

    /// <summary>The cover a bench gets once the moves it was built for are provided for: a centre back, a midfielder, a striker, then wider.</summary>
    private static readonly PlayerPosition[] CoverOrder =
    [
        PlayerPosition.CentreBack,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.RightBack,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.RightWinger,
    ];

    /// <summary>Builds a snapshot from two position-shaped sides.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="home">The home side.</param>
    /// <param name="away">The away side.</param>
    /// <param name="rules">The rules in force, or null for the shipped set.</param>
    public static MatchInputV1 BuildShaped(ulong seed, ShapedSquad home, ShapedSquad away, EngineRulesV2? rules = null) =>
        Frame(seed, rules, home.Side, away.Side);

    /// <summary>
    /// Builds a side whose players are made from the game's own position profiles, with no random jitter: each attribute is the mean plus the
    /// position's bonus, brought back so the ten outfield starters average the mean, and clamped to 1-20.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every starter is wholly at home in his slot of the start shape (the engine's familiarity grade is certain), wide midfielders in a
    /// 4-4-2 and wingers in a 4-3-3. The bench is a keeper and six outfield players: for each target shape, the players the move to it needs
    /// beyond the starters who already fit (so that every slot of the new shape has a natural player), then cover. A bench that cannot
    /// provide for the targets fails loudly rather than leaving a slot to a makeshift. With no randomness the squad depends only on its shape, so
    /// the match seed enters only through the match.
    /// </para>
    /// <para>
    /// The mean is the ability of the outfield players only: the keeper keeps his profile (goalkeeping attributes clamped to 20), as a
    /// generated club's does.
    /// </para>
    /// </remarks>
    /// <param name="clubSeed">1 for the home side, 2 for the away side: it fixes the identities.</param>
    /// <param name="start">The shape the side starts in.</param>
    /// <param name="targets">The shapes the side may move to during the match.</param>
    /// <param name="mean">The mean of the outfield starters' 24 outfield attributes.</param>
    public static ShapedSquad Shaped(int clubSeed, FormationPreset start, IReadOnlyList<FormationPreset> targets, int mean = 16)
    {
        var layout = FormationLayouts.DefaultSlots(start);
        var clubId = Identity(clubSeed * 1_000);
        var standing = layout
            .Select(slot => new ShapeFitPlayer(Identity((clubSeed * 100) + slot.SlotNumber), PositionOf(slot), [], slot))
            .ToArray();
        var outfield = standing.Skip(1).ToArray();
        var offset = OffsetFor([.. outfield.Select(player => player.Position)], mean);

        var drafts = targets.Distinct().Select(target => DraftOf(start, target, outfield)).ToList();
        var bench = BenchFor(drafts, clubSeed);
        var moves = drafts.ToDictionary(draft => draft.Target, draft => MoveOf(start, draft, standing, outfield, bench));

        var squad = new List<MatchParticipantV1>();

        foreach (var player in standing)
        {
            squad.Add(Participant(clubId, player.Id, clubSeed, player.Standing.SlotNumber, EngineVocabulary.Position(player.Position), AttributesFor(player.Position, mean, offset)));
        }

        foreach (var (number, position, id) in bench)
        {
            squad.Add(Participant(clubId, id, clubSeed, number, EngineVocabulary.Position(position), AttributesFor(position, mean, offset)));
        }

        var side = new MatchSideV1
        {
            ClubId = clubId,
            ClubName = clubSeed == 1 ? "Home" : "Away",
            Squad = squad,
            Slots =
            [
                .. layout.Select(slot => new MatchSlotV1
                {
                    SlotNumber = slot.SlotNumber,
                    Family = EngineVocabulary.Family(slot.PositionFamily),
                    Role = EngineVocabulary.Role(slot.Role),
                    X = slot.NormalizedX,
                    Y = slot.NormalizedY,
                    ParticipantId = Identity((clubSeed * 100) + slot.SlotNumber),
                }),
            ],
            Instructions = new MatchInstructionsV1(),
        };

        var reached = outfield.Average(player => AbilityOf(AttributesFor(player.Position, mean, offset)));

        return new ShapedSquad(clubSeed, start, side, reached, moves, Describe(clubSeed, start, standing, bench, mean, offset, reached, moves.Values));
    }

    /// <summary>The position that is wholly at home in a slot: the one of the role's natural positions in the slot's band, on the slot's side.</summary>
    private static PlayerPosition PositionOf(FormationSlot slot)
    {
        var right = slot.NormalizedY >= 5_000;

        return slot.Role switch
        {
            PlayerRole.Goalkeeper => PlayerPosition.Goalkeeper,
            PlayerRole.CentreBack => PlayerPosition.CentreBack,
            PlayerRole.FullBack or PlayerRole.WingBack => right ? PlayerPosition.RightBack : PlayerPosition.LeftBack,
            PlayerRole.DefensiveMidfielder => PlayerPosition.DefensiveMidfielder,
            PlayerRole.CentralMidfielder => PlayerPosition.CentralMidfielder,
            PlayerRole.AttackingMidfielder => PlayerPosition.AttackingMidfielder,
            PlayerRole.WideMidfielder => right ? PlayerPosition.RightMidfielder : PlayerPosition.LeftMidfielder,
            PlayerRole.Winger => right ? PlayerPosition.RightWinger : PlayerPosition.LeftWinger,
            PlayerRole.Striker => PlayerPosition.Striker,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot.Role, "Unknown role."),
        };
    }

    private static PlayerAttributesV1 AttributesFor(PlayerPosition position, int mean, double offset)
    {
        // The keeper has the old laboratory's uniform attributes: his position's profile halves the goals (user decision 2026-10-18), which would
        // make this study's points differences smaller than the match-plan study's. TS_PROFILEGK=1 gives him the profile back.
        if (position == PlayerPosition.Goalkeeper && Environment.GetEnvironmentVariable("TS_PROFILEGK") is not { Length: > 0 })
        {
            return PlayerAttributesV1.Uniform(mean);
        }

        var values = new int[MatchAttributeNames.Count];

        for (var index = 0; index < values.Length; index++)
        {
            var raw = mean + offset + PlayerAttributeProfiles.BonusFor(position, DomainAttributes[index]);

            values[index] = Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), MatchAttributeNames.Min, MatchAttributeNames.Max);
        }

        return PlayerAttributesV1.From(values);
    }

    private static double AbilityOf(PlayerAttributesV1 attributes) => attributes.Values.Take(OutfieldAttributes).Average();

    /// <summary>The offset to the mean that brings the starters' average ability closest to it, the smallest offset on a tie.</summary>
    private static double OffsetFor(IReadOnlyList<PlayerPosition> starters, int mean)
    {
        var bestStep = 0;
        var bestGap = double.MaxValue;

        for (var step = -OffsetSearchHundredths; step <= OffsetSearchHundredths; step++)
        {
            var gap = Math.Abs(starters.Average(position => AbilityOf(AttributesFor(position, mean, step / 100.0))) - mean);

            if (gap < bestGap - 1e-9 || (Math.Abs(gap - bestGap) <= 1e-9 && Math.Abs(step) < Math.Abs(bestStep)))
            {
                bestGap = gap;
                bestStep = step;
            }
        }

        return bestStep / 100.0;
    }

    private sealed record Draft(FormationPreset Target, FormationSlot[] Slots, int[] Starter, List<int> Needs, List<int> Leavers);

    /// <summary>Matches the starters to the target's slots they are natural for, as many as can be and with the least walking; the rest need substitutes.</summary>
    private static Draft DraftOf(FormationPreset start, FormationPreset target, ShapeFitPlayer[] outfield)
    {
        var slots = FormationLayouts.DefaultSlots(target).Where(slot => slot.SlotNumber > 1).ToArray();
        var matched = Solve(
            outfield.Length,
            slots.Length,
            (player, slot) => IsNatural(outfield[player], slots[slot]) ? Walk(outfield[player], slots[slot]) : Forbidden,
            SlotForASubstitute);
        var needs = Enumerable.Range(0, slots.Length).Where(slot => matched[slot] < 0).ToList();
        var leavers = Enumerable.Range(0, outfield.Length).Where(player => !matched.Contains(player)).ToList();

        if (needs.Count != leavers.Count)
        {
            throw new InvalidOperationException($"{start.ToCode()} to {target.ToCode()}: {needs.Count} slots need a substitute but {leavers.Count} starters are left over.");
        }

        return new Draft(target, slots, matched, needs, leavers);
    }

    /// <summary>The bench: the positions every target's move needs (the most any one needs of each), then cover, up to six outfield players.</summary>
    private static List<(int Number, PlayerPosition Position, Guid Id)> BenchFor(IReadOnlyList<Draft> drafts, int clubSeed)
    {
        var needed = new Dictionary<PlayerPosition, int>();

        foreach (var draft in drafts)
        {
            foreach (var group in draft.Needs.GroupBy(slot => PositionOf(draft.Slots[slot])))
            {
                needed[group.Key] = Math.Max(needed.GetValueOrDefault(group.Key), group.Count());
            }
        }

        var positions = needed.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).OrderBy(position => (int)position).ToList();

        if (positions.Count > BenchOutfield)
        {
            throw new InvalidOperationException(
                $"The moves to {string.Join(", ", drafts.Select(draft => draft.Target.ToCode()))} need {positions.Count} outfield substitutes ({string.Join(", ", positions)}), but a bench has room for {BenchOutfield}.");
        }

        for (var cover = 0; positions.Count < BenchOutfield; cover++)
        {
            var next = CoverOrder[cover % CoverOrder.Length];

            if (cover >= CoverOrder.Length || !positions.Contains(next))
            {
                positions.Add(next);
            }
        }

        // The squad's identities: the keeper is substitute 51, the outfield substitutes follow.
        return
        [
            (51, PlayerPosition.Goalkeeper, Identity((clubSeed * 100) + 51)),
            .. positions.Select((position, index) => (52 + index, position, Identity((clubSeed * 100) + 52 + index))),
        ];
    }

    private static ShapedMove MoveOf(
        FormationPreset start,
        Draft draft,
        ShapeFitPlayer[] standing,
        ShapeFitPlayer[] outfield,
        List<(int Number, PlayerPosition Position, Guid Id)> bench)
    {
        var free = bench.Skip(1).ToList();
        var leavers = draft.Leavers.ToList();
        var players = new List<ShapeFitPlayer>();
        var substitutions = new List<MatchRuleSubstitutionV1>();
        var seatOf = new Dictionary<Guid, int>();
        var off = new List<string>();
        var on = new List<string>();

        foreach (var player in outfield)
        {
            seatOf[player.Id] = player.Standing.SlotNumber;
        }

        foreach (var starter in draft.Starter.Where(index => index >= 0))
        {
            players.Add(outfield[starter]);
        }

        foreach (var need in draft.Needs)
        {
            var slot = draft.Slots[need];
            var position = PositionOf(slot);
            var incoming = free.First(candidate => candidate.Position == position);
            var leaver = leavers.OrderBy(index => Walk(outfield[index], slot)).ThenBy(index => index).First();

            free.Remove(incoming);
            leavers.Remove(leaver);
            players.Add(new ShapeFitPlayer(incoming.Id, position, [], outfield[leaver].Standing));
            seatOf[incoming.Id] = outfield[leaver].Standing.SlotNumber;
            substitutions.Add(new MatchRuleSubstitutionV1
            {
                Off = MatchRuleOff.Player,
                OffParticipantId = outfield[leaver].Id,
                On = MatchRuleOn.Player,
                OnParticipantId = incoming.Id,
            });
            off.Add(outfield[leaver].Position.ToString());
            on.Add(position.ToString());
        }

        var natural = Solve(
            players.Count,
            draft.Slots.Length,
            (player, slot) => IsNatural(players[player], draft.Slots[slot]) ? Walk(players[player], draft.Slots[slot]) : Forbidden,
            Forbidden);
        var mismatch = Solve(
            players.Count,
            draft.Slots.Length,
            (player, slot) => -((FormationShapeFit.Certain - FormationShapeFit.FamiliarityOf(players[player], draft.Slots[slot])) * MismatchWeight) - Walk(players[player], draft.Slots[slot]),
            Forbidden);

        var autoFit = FormationShapeFit.Fit(standing, FormationLayouts.DefaultSlots(draft.Target), null);
        var targetSlots = FormationLayouts.DefaultSlots(draft.Target).ToDictionary(slot => slot.SlotNumber);
        var autoOff = outfield.Count(player => FormationShapeFit.FamiliarityOf(player, targetSlots[autoFit[player.Id]]) != FormationShapeFit.Certain);

        IReadOnlyList<MatchSlotOverrideV1> Placements(int[] assignment) =>
            [.. assignment.Select((player, slot) => new MatchSlotOverrideV1(players[player].Id, draft.Slots[slot].SlotNumber))];

        var description = string.Create(
            CultureInfo.InvariantCulture,
            $"{start.ToCode()} to {draft.Target.ToCode()}: {(off.Count == 0 ? "no substitutes needed" : $"off {string.Join(", ", off)}; on {string.Join(", ", on)}")}; every one of the ten in his natural slot; the engine's own fit alone leaves {autoOff} of the ten off-natural");

        return new ShapedMove(draft.Target, substitutions, Placements(natural), Placements(mismatch), seatOf, autoOff, description);
    }

    private static bool IsNatural(ShapeFitPlayer player, FormationSlot slot) => FormationShapeFit.FamiliarityOf(player, slot) == FormationShapeFit.Certain;

    /// <summary>How far a player's seat is from a slot, in the shape fit's own units.</summary>
    private static long Walk(ShapeFitPlayer player, FormationSlot slot) =>
        Math.Abs(player.Standing.NormalizedX - slot.NormalizedX)
        + (Math.Abs(player.Standing.NormalizedY - slot.NormalizedY) * FormationShapeFit.LateralDistancePercent / 100);

    /// <summary>
    /// Assigns players to slots at the least total cost, by dynamic programming over the set of players taken (ten players, so a thousand sets).
    /// A slot may be left unfilled for a fixed cost, which makes the assignment fill as many as the costs allow; a cost of
    /// <see cref="Forbidden"/> rules a pair out.
    /// </summary>
    /// <returns>The player who takes each slot, or -1 for a slot left unfilled.</returns>
    private static int[] Solve(int players, int slots, Func<int, int, long> cost, long unfilled)
    {
        var states = 1 << players;
        var best = new long[(slots + 1) * states];
        var choice = new sbyte[(slots + 1) * states];

        Array.Fill(best, Forbidden);
        best[0] = 0;

        void Relax(int slot, int mask, long value, int player)
        {
            var at = (slot * states) + mask;

            if (value < best[at])
            {
                best[at] = value;
                choice[at] = (sbyte)player;
            }
        }

        for (var slot = 0; slot < slots; slot++)
        {
            for (var mask = 0; mask < states; mask++)
            {
                var here = best[(slot * states) + mask];

                if (here >= Forbidden)
                {
                    continue;
                }

                if (unfilled < Forbidden)
                {
                    Relax(slot + 1, mask, here + unfilled, -1);
                }

                for (var player = 0; player < players; player++)
                {
                    if ((mask & (1 << player)) != 0)
                    {
                        continue;
                    }

                    var price = cost(player, slot);

                    if (price < Forbidden)
                    {
                        Relax(slot + 1, mask | (1 << player), here + price, player);
                    }
                }
            }
        }

        var end = -1;

        for (var mask = 0; mask < states; mask++)
        {
            if (best[(slots * states) + mask] < Forbidden && (end < 0 || best[(slots * states) + mask] < best[(slots * states) + end]))
            {
                end = mask;
            }
        }

        if (end < 0)
        {
            throw new InvalidOperationException("No assignment of the players to the slots is allowed.");
        }

        var result = new int[slots];

        for (var slot = slots; slot >= 1; slot--)
        {
            var player = choice[(slot * states) + end];

            result[slot - 1] = player;

            if (player >= 0)
            {
                end &= ~(1 << player);
            }
        }

        return result;
    }

    private static List<string> Describe(
        int clubSeed,
        FormationPreset start,
        ShapeFitPlayer[] standing,
        List<(int Number, PlayerPosition Position, Guid Id)> bench,
        int mean,
        double offset,
        double reached,
        IEnumerable<ShapedMove> moves)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{(clubSeed == 1 ? "home" : "away")} {start.ToCode()} squad, target mean {mean}, offset {offset:+0.00;-0.00;0.00} brought the ten outfield starters to {reached:F2} (24 outfield attributes)"),
        };

        foreach (var player in standing)
        {
            var attributes = AttributesFor(player.Position, mean, offset);
            var grade = FormationShapeFit.FamiliarityOf(player, player.Standing);

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"   slot {player.Standing.SlotNumber,2} {player.Standing.Role.ToCode(),-22} {player.Position,-20} ability {AbilityOf(attributes),5:F2}   {(grade == FormationShapeFit.Certain ? "natural" : $"NOT natural ({grade})")}"));
        }

        lines.Add("   bench: " + string.Join(", ", bench.Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Position} {(entry.Position == PlayerPosition.Goalkeeper ? string.Empty : AbilityOf(AttributesFor(entry.Position, mean, offset)).ToString("F1", CultureInfo.InvariantCulture))}").Trim())));

        foreach (var move in moves)
        {
            lines.Add("   " + move.Description);
        }

        return lines;
    }
}
