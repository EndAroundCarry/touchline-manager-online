# ADR-0065: The stadium picture is a three-dimensional drawing with sectors, rows and seats in the club's two colours

- **Status:** Accepted
- **Date:** 2026-10-07
- **Stage:** Facilities milestone (web client only)
- **Related:** [ADR-0062](0062-club-stadium-places-levels-and-gate.md) (decision 6, which this supersedes), game rules `STAD-1`, `STAD-2`, `STAD-6`

## Context

ADR-0062 drew each of the ten stadium levels as a flat, top-down SVG: rectangles for stands, one pattern for the seats in
the club's primary colour. A manager could not see a row, a sector or a seat, the second club colour appeared only as a
thin roof outline, and the first level — the one every club starts at — showed terraces and no seats at all, although
every club opens with 1,000 seating, 900 covered and 100 VIP places (`STAD-2`). The product asked for the ground to be
seen as a ground: in three dimensions, with its seats, rows and regions visible, the seats in the club's two colours.

## Decision

**1. The picture is a fixed three-dimensional view, drawn as SVG.** The ground is seen from one corner (turned 33°, 50°
down), the way a management game shows it. A camera module (`stadium-camera`) carries world points to the drawing, so
every shape goes through one projection. It is still a drawing and not a bitmap or a WebGL scene: it is crisp at every
size, costs no download and no dependency, takes the club's colours directly, and its geometry is plain data a unit test
can read. A 3D engine was rejected: it would add a dependency and a canvas the tests and the accessibility story would
have to work around, to draw a view that never moves.

**2. A level is a plan; a drawing is built from the plan.** `stadium-plan` holds the ten levels as data — rows per tier,
terrace or seats, how much of a stand is roofed, where the hospitality boxes are — and `stadium-scene` turns a level into
an ordered list of shapes, far to near. The camera is fixed, so the order is too: main stand and east end, then the pitch
furniture, then west end and opposite stand. Whether a face shows is read from the camera, not written down.

**3. Sectors, rows and seats are drawn.** A stand is cut into sectors by aisles. Every row is a tread (and a riser, when
the camera sees it), and a seat is a dash on a stroked line, so a ground of thousands of seats is a few hundred elements:
the biggest ground is 342 shapes. A terrace is drawn as steps with a standing crowd and crush barriers; the hospitality
boxes as a glass band with frames; a roof as a slab over the back of the covered sectors, with columns behind.

**4. The seats take both of the club's colours.** The first colour is every seat; the second is a dash pattern laid over
it that moves one seat along on each row, so a sector reads as a diagonal band. The second colour is also the roof trim,
the frames of the boxes, half the advertising boards and the flag. A shape names `primary` or `secondary`; the component
resolves them, and a colour that is not plain `#rrggbb` is replaced before it reaches an attribute, as before.

**5. Every level shows every kind of place.** Places are never removed (`STAD-6`) and every club opens with all four kinds
(`STAD-2`), so every level draws standing terraces, open seats, covered seats and hospitality boxes. A stand that is a
terrace stays a terrace; where a level wants seats over one, a seated tier rises above it. The previous plan turned
terraces into seats, which the new plan does not. The picture is still a function of the level alone, so level and
picture cannot disagree (`STAD-1`); the legend beside it carries the real places of each kind from the server.

**6. A kind of place can be found.** Each sector, and the boxes, carry an invisible plane tagged with its kind. The screen
lights the planes of the kind a manager points at, or has chosen in the legend, so "where are my covered seats" is answered
in the picture. The stands are named beside it.

**7. A level is framed to its ground.** The drawing shows the part of the scene that holds the level's fenced ground, in
the drawing's own proportions, so the first level is not a small ground lost in the field the biggest one needs. The
gallery of every level uses a lighter drawing (rows grouped in threes, no planes) of the same ground.

## Consequences

**Positive**

- A manager sees rows, sectors, seats and both colours from the first level, and can locate each kind of place.
- No dependency and no new server contract: the stadium response is unchanged.
- Every property that matters is a unit test on data: levels only grow, no kind of place is ever missing, nothing is drawn
  outside the frame, the seats name only the two colours.

**Negative**

- The picture is the level's ground, not the manager's exact mix of places: a manager who builds only standing places
  still sees the same ground at the same level. The legend states the real figures.
- The view is fixed. Two of the four stands are seen from behind, so their rows are shown more narrowly than the others;
  turning the camera is possible later, since drawing order is the only thing that depends on it.
- The second colour is a pattern, so a club whose two colours are close shows a quiet pattern. The colours are the club's
  own and are not adjusted.

## Alternatives considered

- **Keep the top-down drawing and add rows.** Rejected: a plan view shows no height, so a roof, a second tier and a terrace
  step could not be told apart, and the request was for a three-dimensional picture.
- **Three.js or another WebGL library.** Rejected, as above.
- **Bitmap sprites per level.** Rejected for the reason ADR-0062 gave: ten images times every club colour pair.
- **Draw the picture from the exact places of each kind.** Rejected for now: the level plan is what makes the picture one
  ground that grows. It would need a rule for how a count becomes a stand, which belongs to its own decision.
