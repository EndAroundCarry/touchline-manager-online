using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Competition;

/// <summary>One match in a generated schedule: who hosts whom in a round.</summary>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
public sealed record SchedulePairing(Guid HomeClubId, Guid AwayClubId);

/// <summary>One round of a generated schedule.</summary>
/// <param name="RoundNumber">The round, 1-based.</param>
/// <param name="Pairings">Every match in the round, in a deterministic order.</param>
public sealed record ScheduleRound(int RoundNumber, IReadOnlyList<SchedulePairing> Pairings);

/// <summary>
/// Generates a double round-robin fixture list with the circle (Berger) method, mirrored for the second
/// half (`CAL-8`).
/// </summary>
/// <remarks>
/// <para>
/// The first half is one full round-robin: every club meets every other exactly once, and every club plays
/// exactly once per round. The second half replays each first-half pairing with the venues swapped, which
/// is what makes "each pair meets once home and once away" true by construction rather than by a
/// correction pass — and what makes a club's home and away counts exactly equal (`CAL-9`).
/// </para>
/// <para>
/// The draw is a pure function of the clubs, their order, and the seed. Clubs are shuffled by a
/// <see cref="Pcg32"/> stream seeded from the division-season's stored schedule seed, so the same seed
/// reproduces the same fixture list and a different seed produces a visibly different one. The caller must
/// pass the clubs in a <em>stable</em> order it controls — the seeder uses identity-generation order — and
/// never in id order, because ids are UUIDv7 and differ per generation run while the logical schedule must
/// not.
/// </para>
/// <para>
/// No method here reads a clock, a database, or <c>System.Random</c>; the whole fixture list can be
/// regenerated from the recorded seed for audit (`CAL-8`, `PYR-14`).
/// </para>
/// </remarks>
public static class RoundRobinSchedule
{
    /// <summary>The version of the generation algorithm, folded into a world run's input hash.</summary>
    public const string Version = "schedule-gen-v1";

    /// <summary>A club hosted its previous fixture.</summary>
    private const int Hosted = 1;

    /// <summary>A club visited in its previous fixture.</summary>
    private const int Visited = -1;

    /// <summary>Generates the whole 34-round list for a division.</summary>
    /// <param name="clubIds">The clubs, in a stable order, an even number of at least two.</param>
    /// <param name="seed">The schedule seed from the division-season (`CAL-8`).</param>
    /// <returns>Every round in order, 1 to 2 × (clubs − 1).</returns>
    public static IReadOnlyList<ScheduleRound> Generate(IReadOnlyList<Guid> clubIds, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(clubIds);

        if (clubIds.Count < 2 || clubIds.Count % 2 != 0)
        {
            throw new ArgumentException(
                "A round-robin needs an even number of at least two clubs.",
                nameof(clubIds));
        }

        if (clubIds.Distinct().Count() != clubIds.Count)
        {
            throw new ArgumentException("Every club must appear exactly once.", nameof(clubIds));
        }

        var order = Shuffled(Range(clubIds.Count), new Pcg32(seed));

        var clubs = clubIds.Count;
        var half = clubs / 2;
        var roundsInHalf = clubs - 1;

        var fixedClub = order[0];
        var rotating = order[1..];

        // Home and away are decided round by round, alternating a club's venue wherever the pairing allows
        // and otherwise handing the host's role to whoever has hosted less. A per-club home/away counter
        // alone leaves long runs; alternating on the previous round's venue is what keeps them within
        // CAL-9's bound.
        var venues = new int[clubs];
        var homeCounts = new int[clubs];

        var schedule = new List<ScheduleRound>(roundsInHalf * 2);

        for (var round = 0; round < roundsInHalf; round++)
        {
            // The circle: the fixed club stays put and the rest rotate by one each round, which is what
            // makes every pair meet exactly once without a duplicate check.
            var arrangement = new int[clubs];
            arrangement[0] = fixedClub;

            for (var slot = 0; slot < rotating.Length; slot++)
            {
                arrangement[slot + 1] = rotating[(slot + round) % rotating.Length];
            }

            var nextVenues = (int[])venues.Clone();
            var pairings = new List<SchedulePairing>(half);

            for (var index = 0; index < half; index++)
            {
                var first = arrangement[index];
                var second = arrangement[clubs - 1 - index];

                var firstIsHome = FirstHosts(first, second, venues, homeCounts);

                pairings.Add(new SchedulePairing(
                    clubIds[firstIsHome ? first : second],
                    clubIds[firstIsHome ? second : first]));

                nextVenues[firstIsHome ? first : second] = Hosted;
                nextVenues[firstIsHome ? second : first] = Visited;
                homeCounts[firstIsHome ? first : second]++;
            }

            venues = nextVenues;

            schedule.Add(new ScheduleRound(round + 1, pairings));
        }

        // The second half replays the first with the venues swapped, in reverse round order. Reversing is
        // what keeps the boundary honest: the last first-half round and the first second-half round are the
        // same pairing with opposite venues, so a club never carries a run of the same venue across the
        // halfway point, and a run inside either half matches the other half's exactly.
        for (var round = 0; round < roundsInHalf; round++)
        {
            var source = roundsInHalf - 1 - round;

            var mirrored = schedule[source].Pairings
                .Select(pairing => new SchedulePairing(pairing.AwayClubId, pairing.HomeClubId))
                .ToList();

            schedule.Add(new ScheduleRound(round + 1 + roundsInHalf, mirrored));
        }

        return schedule;
    }

    /// <summary>Whether the first of two clubs hosts, alternating wherever the pairing allows.</summary>
    private static bool FirstHosts(int first, int second, int[] venues, int[] homeCounts)
    {
        var firstVisited = venues[first] == Visited;
        var secondVisited = venues[second] == Visited;

        // Whoever played away last round hosts, when exactly one of them did. This is the rule that keeps
        // a club from a run of the same venue.
        if (firstVisited != secondVisited)
        {
            return firstVisited;
        }

        // Otherwise the club that has hosted less often does, and if they are level the lower slot hosts —
        // a stable tie-break, so the schedule never depends on anything but the clubs and the seed.
        return homeCounts[first] != homeCounts[second]
            ? homeCounts[first] < homeCounts[second]
            : first < second;
    }

    private static int[] Range(int count)
    {
        var values = new int[count];

        for (var index = 0; index < count; index++)
        {
            values[index] = index;
        }

        return values;
    }

    private static int[] Shuffled(int[] values, Pcg32 random)
    {
        // Fisher-Yates, in a fixed direction, so the permutation is reproducible from the seed.
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swap = random.NextInt(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }

        return values;
    }
}
