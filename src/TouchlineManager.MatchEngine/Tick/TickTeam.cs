using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// One side's bodies on the pitch, in the order the tick engine's AI reads them (`tick-engine-v1`, Milestone 8).
/// </summary>
/// <remarks>
/// <para>
/// A <em>seat</em> is a place in the AI's arrays. Seat 0 is the goalkeeper (every AI module assumes it), the rest follow in
/// slot order, and the arrays are packed: a player sent off leaves his seat, the ones behind close up, and every module simply sees
/// a shorter side. A seat is therefore not a stable name. The stable name is the <em>slot entity</em> (a side's slot number, which
/// is also the presentation's <c>H1</c>…<c>A11</c>): a substitute takes the seat and the entity of the man he replaces, and keeps his
/// place on the pitch.
/// </para>
/// <para>
/// Every array is allocated once, at the side's full size, so the tick loop allocates nothing. The side's <see cref="SideRuntime"/>
/// stays the authority on who is on the pitch and in what condition (substitutions and cards are decided by the possession
/// engine's planners, which work on it); <see cref="Sync"/> brings the seats into line with it.
/// </para>
/// </remarks>
internal sealed class TickTeam
{
    /// <summary>The seats a side has.</summary>
    public const int Capacity = TickTacticalGeometry.TeamSize;

    /// <summary>The condition lost per basis point of energy spent: a quarter of the tank over a match is about a third of the condition.</summary>
    public const int ConditionPerEnergyPercent = 130;

    private readonly SideRuntime _runtime;
    private readonly int[] _startEnergy = new int[Capacity];
    private readonly int[] _startCondition = new int[Capacity];

    /// <summary>Initializes the side from its runtime, with the bodies not yet placed.</summary>
    /// <param name="runtime">The side's runtime state.</param>
    public TickTeam(SideRuntime runtime)
    {
        _runtime = runtime;
        IsHome = runtime.Which == MatchSide.Home;
        Style = TickTeamStyle.From(runtime.Instructions);

        var active = runtime.Active.OrderBy(slot => slot.Slot.Family == MatchPositionFamily.Goalkeeper ? 0 : 1)
            .ThenBy(slot => slot.Slot.SlotNumber)
            .ToList();

        Count = active.Count;

        for (var seat = 0; seat < Count; seat++)
        {
            Fill(seat, active[seat]);
        }
    }

    /// <summary>Gets whether the side is the home side, which attacks towards high X.</summary>
    public bool IsHome { get; }

    /// <summary>Gets which end the side plays.</summary>
    public MatchSide Side => IsHome ? MatchSide.Home : MatchSide.Away;

    /// <summary>Gets the side's runtime state.</summary>
    public SideRuntime Runtime => _runtime;

    /// <summary>Gets the shape the side's instructions give its block.</summary>
    public TickTeamStyle Style { get; }

    /// <summary>Gets how many players are on the pitch.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the bodies, by seat.</summary>
    public TickPlayerState[] Body { get; } = new TickPlayerState[Capacity];

    /// <summary>Gets the athletic limits, by seat.</summary>
    public TickPlayerProfile[] Profile { get; } = new TickPlayerProfile[Capacity];

    /// <summary>Gets the decision attributes, by seat.</summary>
    public TickPlayerSkills[] Skills { get; } = new TickPlayerSkills[Capacity];

    /// <summary>Gets the board positions, by seat.</summary>
    public TickAnchorSpec[] Spec { get; } = new TickAnchorSpec[Capacity];

    /// <summary>Gets each seat's player, by seat.</summary>
    public Guid[] Id { get; } = new Guid[Capacity];

    /// <summary>Gets each seat's slot number, 1…11, by seat.</summary>
    public int[] SlotNumber { get; } = new int[Capacity];

    /// <summary>Gets the dynamic anchors, by seat.</summary>
    public SpatialPoint[] Anchor { get; } = new SpatialPoint[Capacity];

    /// <summary>Gets the pace each seat travels at, in basis points of top speed (0 is the holding pace).</summary>
    public int[] Pace { get; } = new int[Capacity];

    /// <summary>Gets the move each seat asks for, by seat, when <see cref="HasIntent"/> is set.</summary>
    public TickMoveIntent[] Intent { get; } = new TickMoveIntent[Capacity];

    /// <summary>Gets whether a seat's move is given directly (a goalkeeper's dive) rather than steered to its anchor.</summary>
    public bool[] HasIntent { get; } = new bool[Capacity];

    /// <summary>Gets the attacking orders of the current tick, by seat.</summary>
    public TickAttackingOrder[] AttackOrders { get; } = new TickAttackingOrder[Capacity];

    /// <summary>Gets the defensive orders of the current tick, by seat.</summary>
    public TickDefensiveOrder[] DefenceOrders { get; } = new TickDefensiveOrder[Capacity];

    /// <summary>Gets or sets the X of the point the block's shape follows, in pitch units: the ball, smoothed and looking ahead (`tick-film-v1`, Milestone 5).</summary>
    public int RefX { get; set; } = SpatialPitch.PitchLength / 2;

    /// <summary>Gets or sets the Y of the point the block's shape follows, in pitch units.</summary>
    public int RefY { get; set; } = SpatialPitch.GoalYCenter;

    /// <summary>Gets or sets how much of the block's shape is the one with the ball, in basis points: it ramps after a turnover instead of switching.</summary>
    public int PossessionBlend { get; set; }

    /// <summary>
    /// Gets where each seat is to walk about, as an offset from the point he is sent to, in pitch units, when <see cref="HasOffset"/> is
    /// set: a free square for an attacker, a step goal-side for a defender. It is worked out every ten ticks, a seat at a time.
    /// </summary>
    public SpatialPoint[] MicroOffset { get; } = new SpatialPoint[Capacity];

    /// <summary>Gets whether a seat has a <see cref="MicroOffset"/> on hand. It outlasts the tick it was worked out on.</summary>
    public bool[] HasOffset { get; } = new bool[Capacity];

    /// <summary>Gets whether a seat walks about his <see cref="MicroOffset"/> this tick: only a man with no job beyond holding his place does.</summary>
    public bool[] UseMicro { get; } = new bool[Capacity];

    /// <summary>Gets the share of top speed each seat walks to his micro-target at, in basis points (0 is a walk).</summary>
    public int[] MicroPace { get; } = new int[Capacity];

    /// <summary>Gets whether a seat is in a duel or on the ball this tick, and so does not keep clear of opponents.</summary>
    public bool[] Engaged { get; } = new bool[Capacity];

    /// <summary>Gets or sets the players who supported the carrier last tick, one bit a seat.</summary>
    public int SupporterMask { get; set; }

    /// <summary>Gets or sets the players who were running behind the line last tick, one bit a seat.</summary>
    public int RunnerMask { get; set; }

    /// <summary>Gets or sets the seat that pressed last tick, or -1.</summary>
    public int PreviousPresser { get; set; } = -1;

    /// <summary>Gets or sets whether the goalkeeper was charging out last tick.</summary>
    public bool KeeperRushing { get; set; }

    /// <summary>Gets whether the seat is the goalkeeper's.</summary>
    /// <param name="seat">The seat.</param>
    public bool IsKeeper(int seat) => Spec[seat].Family == MatchPositionFamily.Goalkeeper;

    /// <summary>Gets the slot entity (0..10 home, 11..21 away) a seat stands in.</summary>
    /// <param name="seat">The seat.</param>
    public int Entity(int seat) => (IsHome ? 0 : TickMatchRecording.Entities / 2) + SlotNumber[seat] - 1;

    /// <summary>Gets the seat a participant stands in, or -1.</summary>
    /// <param name="id">The participant.</param>
    public int SeatOf(Guid id)
    {
        for (var seat = 0; seat < Count; seat++)
        {
            if (Id[seat] == id)
            {
                return seat;
            }
        }

        return -1;
    }

    /// <summary>Gets the seat that stands in a slot, or -1.</summary>
    /// <param name="slotNumber">The slot, 1…11.</param>
    public int SeatOfSlot(int slotNumber)
    {
        for (var seat = 0; seat < Count; seat++)
        {
            if (SlotNumber[seat] == slotNumber)
            {
                return seat;
            }
        }

        return -1;
    }

    /// <summary>Gets the active slot a seat's player fills, or null when he has left the pitch.</summary>
    /// <param name="seat">The seat.</param>
    public ActiveSlot? ActiveOf(int seat)
    {
        foreach (var slot in _runtime.Active)
        {
            if (slot.Participant.ParticipantId == Id[seat])
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>Puts every body on its kick-off position, standing, facing the other goal.</summary>
    /// <param name="homeKicksOff">Whether the home side takes the kick-off.</param>
    public void PlaceForKickOff(bool homeKicksOff)
    {
        var heading = IsHome ? 0 : TickTrigonometry.HalfTurn;
        var hasBall = homeKicksOff == IsHome;

        for (var seat = 0; seat < Count; seat++)
        {
            var anchor = TickTacticalGeometry.Resolve(
                Spec[seat],
                Style,
                IsHome,
                hasBall,
                SpatialPitch.PitchLength / 2,
                SpatialPitch.GoalYCenter);

            var halfLimit = TickSetPieces.OwnHalfLimit;
            var x = IsHome ? Math.Min(anchor.X, halfLimit) : Math.Max(anchor.X, SpatialPitch.PitchLength - halfLimit);

            Body[seat] = TickPlayerState.Standing(x, anchor.Y, heading, ConditionOf(seat)) with { Energy = Body[seat].Energy };
        }

        RefX = SpatialPitch.PitchLength / 2;
        RefY = SpatialPitch.GoalYCenter;
        PossessionBlend = hasBall ? 10_000 : 0;
        Array.Clear(HasOffset);
    }

    /// <summary>
    /// Brings the seats into line with the runtime after a substitution or a sending-off: a substitute takes his man's seat, a
    /// player who left for good gives his up.
    /// </summary>
    /// <param name="recording">The recording to note roster changes in, or null.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Sync(TickMatchRecording? recording)
    {
        var changed = false;

        for (var seat = Count - 1; seat >= 0; seat--)
        {
            if (ActiveOf(seat) is not null)
            {
                continue;
            }

            changed = true;

            ActiveSlot? replacement = null;

            foreach (var slot in _runtime.Active)
            {
                if (slot.Slot.SlotNumber == SlotNumber[seat] && SeatOf(slot.Participant.ParticipantId) < 0)
                {
                    replacement = slot;

                    break;
                }
            }

            if (replacement is not null)
            {
                var keepBody = Body[seat];

                Fill(seat, replacement);
                HasOffset[seat] = false;

                // He takes his man's place on the pitch.
                Body[seat] = keepBody with
                {
                    Energy = _startEnergy[seat],
                    Lockout = 0,
                    Speed = 0,
                };

                recording?.AddRoster(Entity(seat), replacement.Participant.ParticipantId);

                continue;
            }

            recording?.AddRoster(Entity(seat), Guid.Empty);
            Remove(seat);
        }

        return changed;
    }

    /// <summary>
    /// Writes each player's condition back to the runtime from the energy he has spent (the possession engine's planners read it):
    /// the condition he started with, less <see cref="ConditionPerEnergyPercent"/> of the share of the tank he has used.
    /// </summary>
    public void PushCondition()
    {
        for (var seat = 0; seat < Count; seat++)
        {
            for (var index = 0; index < _runtime.Active.Count; index++)
            {
                var slot = _runtime.Active[index];

                if (slot.Participant.ParticipantId != Id[seat])
                {
                    continue;
                }

                var spent = (int)((long)(_startEnergy[seat] - Body[seat].Energy) * 10_000 / TickPlayerPhysics.EnergyFull);
                var condition = Math.Clamp(_startCondition[seat] - (spent * ConditionPerEnergyPercent / 100), 0, 10_000);

                _runtime.Active[index] = slot with
                {
                    Condition = slot.Condition.WithConditionDelta(condition - slot.Condition.ConditionBasisPoints),
                };

                break;
            }
        }
    }

    /// <summary>Gets the condition a seat's player started his spell with, in basis points.</summary>
    /// <param name="seat">The seat.</param>
    public int ConditionOf(int seat) => _startCondition[seat];

    /// <summary>Gives back at the interval the share of the tank that the rules' condition recovery is worth.</summary>
    /// <param name="recovery">The condition recovered, in basis points.</param>
    public void Rest(int recovery)
    {
        var restored = (int)((long)recovery * 100 / ConditionPerEnergyPercent) * (TickPlayerPhysics.EnergyFull / 10_000);

        for (var seat = 0; seat < Count; seat++)
        {
            Body[seat].Energy = Math.Min(_startEnergy[seat], Body[seat].Energy + restored);
        }
    }

    private void Fill(int seat, ActiveSlot slot)
    {
        var attributes = slot.Participant.Attributes;
        var condition = slot.Condition.ConditionBasisPoints;

        Id[seat] = slot.Participant.ParticipantId;
        SlotNumber[seat] = slot.Slot.SlotNumber;
        Spec[seat] = TickAnchorSpec.From(slot.Slot);
        Skills[seat] = TickPlayerSkills.Rated(attributes);
        Profile[seat] = TickPlayerProfile.From(attributes, slot.Slot.Family == MatchPositionFamily.Goalkeeper);
        _startCondition[seat] = condition;
        _startEnergy[seat] = Math.Clamp(condition, 0, 10_000) * (TickPlayerPhysics.EnergyFull / 10_000);
        Body[seat] = new TickPlayerState { Energy = _startEnergy[seat] };
    }

    private void Remove(int seat)
    {
        var wasKeeper = IsKeeper(seat);

        for (var index = seat; index < Count - 1; index++)
        {
            Body[index] = Body[index + 1];
            Profile[index] = Profile[index + 1];
            Skills[index] = Skills[index + 1];
            Spec[index] = Spec[index + 1];
            Id[index] = Id[index + 1];
            SlotNumber[index] = SlotNumber[index + 1];
            _startEnergy[index] = _startEnergy[index + 1];
            _startCondition[index] = _startCondition[index + 1];
            MicroOffset[index] = MicroOffset[index + 1];
            HasOffset[index] = HasOffset[index + 1];
            MicroPace[index] = MicroPace[index + 1];
        }

        Count--;
        SupporterMask = 0;
        RunnerMask = 0;
        PreviousPresser = -1;
        KeeperRushing = false;
        Array.Clear(HasOffset);

        if (wasKeeper && Count > 0)
        {
            Promote();
        }
    }

    /// <summary>An outfield player takes the gloves: the one with the best hands, who stands where the goalkeeper's anchor is.</summary>
    private void Promote()
    {
        var best = 0;
        var bestScore = int.MinValue;
        var keeperSpec = new TickAnchorSpec(Spec[0].OwnX, 3_500, MatchPositionFamily.Goalkeeper);

        for (var seat = 0; seat < Count; seat++)
        {
            var score = Skills[seat].Handling + Skills[seat].Reflexes + Skills[seat].OneOnOnes;

            if (score > bestScore)
            {
                bestScore = score;
                best = seat;
            }
        }

        // Swap him into seat 0 so every module's "index 0 is the goalkeeper" still holds.
        if (best != 0)
        {
            (Body[0], Body[best]) = (Body[best], Body[0]);
            (Profile[0], Profile[best]) = (Profile[best], Profile[0]);
            (Skills[0], Skills[best]) = (Skills[best], Skills[0]);
            (Spec[0], Spec[best]) = (Spec[best], Spec[0]);
            (Id[0], Id[best]) = (Id[best], Id[0]);
            (SlotNumber[0], SlotNumber[best]) = (SlotNumber[best], SlotNumber[0]);
            (_startEnergy[0], _startEnergy[best]) = (_startEnergy[best], _startEnergy[0]);
            (_startCondition[0], _startCondition[best]) = (_startCondition[best], _startCondition[0]);
        }

        Spec[0] = keeperSpec with { OwnX = Math.Min(keeperSpec.OwnX, 600) };
    }
}
