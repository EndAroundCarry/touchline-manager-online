"use strict";
(() => {
  // src/app/features/match-viewer/renderer/keyframe-interpolator.ts
  var PITCH_SCALE = 1e4;
  var ALTITUDE_SCALE = 100;
  function catmullRom(p0, p1, p2, p3, t) {
    const t2 = t * t;
    const t3 = t2 * t;
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
  }
  function sampleAt(keyframes, timeMs) {
    if (keyframes.length === 0) {
      return null;
    }
    const first = keyframes[0];
    const last = keyframes[keyframes.length - 1];
    if (timeMs <= first.timeMilliseconds) {
      return sampleFrom(first);
    }
    if (timeMs >= last.timeMilliseconds) {
      return sampleFrom(last);
    }
    for (let index = 1; index < keyframes.length; index += 1) {
      const to = keyframes[index];
      if (timeMs > to.timeMilliseconds) {
        continue;
      }
      const from = keyframes[index - 1];
      const span = to.timeMilliseconds - from.timeMilliseconds;
      if (span <= 0) {
        return sampleFrom(to);
      }
      const progress = (timeMs - from.timeMilliseconds) / span;
      if (keyframes.length === 2) {
        return {
          x: from.x + (to.x - from.x) * progress,
          y: from.y + (to.y - from.y) * progress,
          z: clampAltitude(altitudeOf(from) + (altitudeOf(to) - altitudeOf(from)) * progress),
          speed: from.speed ?? 0,
          action: from.action ?? null
        };
      }
      const before = keyframes[index - 2] ?? from;
      const after = keyframes[index + 1] ?? to;
      return {
        x: clampCoordinate(catmullRom(before.x, from.x, to.x, after.x, progress)),
        y: clampCoordinate(catmullRom(before.y, from.y, to.y, after.y, progress)),
        z: clampAltitude(
          catmullRom(
            altitudeOf(before),
            altitudeOf(from),
            altitudeOf(to),
            altitudeOf(after),
            progress
          )
        ),
        // The speed and the action belong to the keyframe the segment starts on: the strike is a moment the
        // shooter reached, and the effect it triggers plays out over the movement that follows it.
        speed: from.speed ?? 0,
        action: from.action ?? null
      };
    }
    return sampleFrom(last);
  }
  function frameAt(entities2, tracks2, timeMs) {
    const byId = new Map(tracks2.map((track2) => [track2.entityId, track2.keyframes]));
    return entities2.map((entity) => {
      const sample = sampleAt(byId.get(entity.id) ?? [], timeMs);
      return sample === null ? { entity, position: entity.anchor, z: 0, speed: 0, action: null } : {
        entity,
        position: { x: sample.x, y: sample.y },
        z: sample.z,
        speed: sample.speed,
        action: sample.action
      };
    });
  }
  function sampleFrom(keyframe) {
    return {
      x: keyframe.x,
      y: keyframe.y,
      z: altitudeOf(keyframe),
      speed: keyframe.speed ?? 0,
      action: keyframe.action ?? null
    };
  }
  function altitudeOf(keyframe) {
    return keyframe.z ?? 0;
  }
  function clampCoordinate(value) {
    return Math.min(PITCH_SCALE, Math.max(0, value));
  }
  function clampAltitude(value) {
    return Math.min(ALTITUDE_SCALE, Math.max(0, value));
  }

  // src/app/features/match-viewer/renderer/pitch-layout.ts
  var PITCH_ASPECT_RATIO = 105 / 68;
  var PITCH_COORDINATE_SCALE = 1e4;
  var PITCH_LENGTH_METRES = 105;
  var PITCH_WIDTH_METRES = 68;
  var PITCH_INSET = 0.05;
  function pitchRect(width, height) {
    const availableWidth = Math.max(0, width * (1 - PITCH_INSET * 2));
    const availableHeight = Math.max(0, height * (1 - PITCH_INSET * 2));
    let pitchWidth = availableWidth;
    let pitchHeight = pitchWidth / PITCH_ASPECT_RATIO;
    if (pitchHeight > availableHeight) {
      pitchHeight = availableHeight;
      pitchWidth = pitchHeight * PITCH_ASPECT_RATIO;
    }
    return {
      x: (width - pitchWidth) / 2,
      y: (height - pitchHeight) / 2,
      width: pitchWidth,
      height: pitchHeight
    };
  }
  function toCanvasPoint(point, rect) {
    return {
      x: rect.x + point.x / PITCH_COORDINATE_SCALE * rect.width,
      y: rect.y + point.y / PITCH_COORDINATE_SCALE * rect.height
    };
  }
  var PITCH_METRES = {
    centreCircleRadius: 9.15,
    centreSpotRadius: 0.2,
    penaltyBoxDepth: 16.5,
    penaltyBoxHeight: 40.32,
    sixYardBoxDepth: 5.5,
    sixYardBoxHeight: 18.32,
    penaltySpotInset: 11,
    penaltySpotRadius: 0.2,
    goalWidth: 7.32,
    goalDepth: 2,
    cornerArcRadius: 1
  };
  var STRIPE_COUNT = 10;
  function metresAcrossPitch(rect, metres) {
    return metres / PITCH_LENGTH_METRES * rect.width;
  }
  function metresDownPitch(rect, metres) {
    return metres / PITCH_WIDTH_METRES * rect.height;
  }
  function pitchGeometry(rect) {
    return {
      centreX: rect.x + rect.width / 2,
      centreY: rect.y + rect.height / 2,
      centreCircleRadius: metresDownPitch(rect, PITCH_METRES.centreCircleRadius),
      centreSpotRadius: Math.max(1.5, metresDownPitch(rect, PITCH_METRES.centreSpotRadius)),
      penaltyBoxDepth: metresAcrossPitch(rect, PITCH_METRES.penaltyBoxDepth),
      penaltyBoxHeight: metresDownPitch(rect, PITCH_METRES.penaltyBoxHeight),
      sixYardBoxDepth: metresAcrossPitch(rect, PITCH_METRES.sixYardBoxDepth),
      sixYardBoxHeight: metresDownPitch(rect, PITCH_METRES.sixYardBoxHeight),
      penaltySpotInset: metresAcrossPitch(rect, PITCH_METRES.penaltySpotInset),
      penaltySpotRadius: Math.max(1.5, metresDownPitch(rect, PITCH_METRES.penaltySpotRadius)),
      goalWidth: metresDownPitch(rect, PITCH_METRES.goalWidth),
      goalDepth: metresAcrossPitch(rect, PITCH_METRES.goalDepth),
      cornerArcRadius: metresDownPitch(rect, PITCH_METRES.cornerArcRadius),
      stripeWidth: rect.width / STRIPE_COUNT,
      stripeCount: STRIPE_COUNT,
      lineWidth: Math.max(1, rect.height / 220)
    };
  }
  function altitudeLift(rect, z) {
    return clampAltitude2(z) / 100 * rect.height * 0.3;
  }
  function altitudeScale(z) {
    return 1 + clampAltitude2(z) / 100 * 0.9;
  }
  function clampAltitude2(z) {
    return Number.isFinite(z) ? Math.min(100, Math.max(0, z)) : 0;
  }

  // src/app/features/match-viewer/renderer/renderer-effects.ts
  var STRIKE_ACTIONS = /* @__PURE__ */ new Set(["shot", "penalty", "free_kick"]);
  var TACKLE_ACTIONS = /* @__PURE__ */ new Set(["tackle", "duel"]);
  var GOAL_TEMPLATES = /* @__PURE__ */ new Set(["match.goal", "match.penalty.goal"]);
  var GOAL_OUTCOMES = /* @__PURE__ */ new Set(["goal", "penalty_goal"]);
  var DUEL_DISTANCE = 380;
  var DUEL_BALL_DISTANCE = 900;
  var POSSESSION_DISTANCE = 900;
  function isStrikeAction(action) {
    return action !== null && action !== void 0 && STRIKE_ACTIONS.has(action);
  }
  function isTackleAction(action) {
    return action !== null && action !== void 0 && TACKLE_ACTIONS.has(action);
  }
  function duelClashes(frame2, maxDistance = DUEL_DISTANCE, ballDistance = DUEL_BALL_DISTANCE) {
    const ball = frame2.find((item) => item.entity.isBall);
    const players = frame2.filter((item) => !item.entity.isBall && item.entity.side !== null);
    const clashes = [];
    for (let first = 0; first < players.length; first += 1) {
      for (let second = first + 1; second < players.length; second += 1) {
        const one = players[first];
        const other = players[second];
        if (one.entity.side === other.entity.side) {
          continue;
        }
        const midpoint = {
          x: (one.position.x + other.position.x) / 2,
          y: (one.position.y + other.position.y) / 2
        };
        const tagged = isTackleAction(one.action) || isTackleAction(other.action);
        const separation = Math.hypot(
          one.position.x - other.position.x,
          one.position.y - other.position.y
        );
        if (!tagged && separation > maxDistance) {
          continue;
        }
        if (ball !== void 0 && distance(midpoint, ball.position) > ballDistance) {
          continue;
        }
        clashes.push({
          x: midpoint.x,
          y: midpoint.y,
          // A stable offset per duel, so two contests on opposite flanks are not drawn pulsing in lockstep.
          phase: (Math.round(midpoint.x) + Math.round(midpoint.y)) % 1e3
        });
      }
    }
    return clashes;
  }
  function nearestPlayerToBall(frame2, maxDistance = POSSESSION_DISTANCE) {
    const ball = frame2.find((item) => item.entity.isBall);
    if (ball === void 0) {
      return null;
    }
    let nearest = null;
    let nearestDistance = maxDistance;
    for (const item of frame2) {
      if (item.entity.isBall || item.entity.side === null) {
        continue;
      }
      const distanceToBall = distance(item.position, ball.position);
      if (distanceToBall <= nearestDistance) {
        nearest = item;
        nearestDistance = distanceToBall;
      }
    }
    return nearest;
  }
  function trailStrength(z, speed2) {
    return Math.min(1, Math.max(speed2 / 3e3, z / 45));
  }
  function celebrationStartMilliseconds(highlight2) {
    if (!GOAL_OUTCOMES.has(highlight2.outcomeCode)) {
      return null;
    }
    const outcome = (highlight2.commentary ?? []).find((line) => GOAL_TEMPLATES.has(line.templateKey));
    return outcome?.timeMilliseconds ?? Math.round(highlight2.durationMilliseconds * 0.7);
  }
  function distance(one, other) {
    return Math.hypot(one.x - other.x, one.y - other.y);
  }

  // src/app/features/match-viewer/renderer/canvas-match-renderer.ts
  var CanvasMatchRenderer = class {
    /** Initializes the renderer for one highlight on one canvas. */
    constructor(canvas2, highlight2, options = {}) {
      this.canvas = canvas2;
      const context = canvas2.getContext("2d");
      if (context === null) {
        throw new Error("A highlight cannot be drawn: the canvas has no 2D context.");
      }
      this.context = context;
      this.entities = toRendererEntities(highlight2.entities);
      this.tracks = toRendererTracks(highlight2.tracks);
      this.ballTrack = highlight2.tracks.find((track2) => track2.entityId === BALL_ENTITY_ID)?.keyframes ?? null;
      this.kits = {
        home: kitFor(options.kits?.home, highlight2.homeColour),
        away: kitFor(options.kits?.away, highlight2.awayColour)
      };
      this.cards = options.cards ?? /* @__PURE__ */ new Map();
      this.reducedMotion = options.reducedMotion ?? false;
      this.celebrateStart = celebrationStartMilliseconds(highlight2);
      this.canvas.addEventListener("pointermove", this.pointerListener);
      this.canvas.addEventListener("pointerleave", this.pointerLeaveListener);
    }
    canvas;
    context;
    entities;
    tracks;
    ballTrack;
    kits;
    cards;
    reducedMotion;
    celebrateStart;
    cssWidth = 0;
    cssHeight = 0;
    lastTimeMs = 0;
    hoveredEntityId = null;
    metricsValue = {
      entities: 0,
      interpolated: 0,
      effects: 0,
      milliseconds: 0
    };
    /** Gets how much the last frame drew, for the renderer's own instrumentation. */
    get metrics() {
      return this.metricsValue;
    }
    /** Draws the highlight at an animation moment. */
    render(timeMs) {
      const started = now();
      const rect = this.resize();
      this.lastTimeMs = timeMs;
      this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
      this.drawPitch(rect);
      const frame2 = frameAt(this.entities, this.tracks, timeMs);
      const players = frame2.filter((item) => !item.entity.isBall);
      const ball = frame2.find((item) => item.entity.isBall);
      const clashes = duelClashes(frame2);
      const possession = nearestPlayerToBall(frame2);
      let effects = clashes.length;
      const radius = playerRadius(rect);
      for (const player of players) {
        this.drawPlayer(rect, player, radius);
      }
      for (const clash of clashes) {
        this.drawClash(rect, clash.x, clash.y, clash.phase);
      }
      if (ball !== void 0) {
        const strength = trailStrength(ball.z, ball.speed);
        if (strength > TRAIL_MIN_STRENGTH) {
          this.drawTrail(rect, strength);
          effects += 1;
        }
        if (this.isStriking(ball, players)) {
          this.drawShotLine(rect, ball, timeMs);
          effects += 1;
        }
        this.drawBall(rect, ball);
      }
      if (possession !== null) {
        this.drawNameTag(rect, possession, radius);
      }
      if (this.hoveredEntityId !== null) {
        const hovered = players.find((item) => item.entity.id === this.hoveredEntityId);
        if (hovered !== void 0) {
          this.drawHover(rect, hovered, radius);
        }
      }
      if (this.celebrateStart !== null && timeMs >= this.celebrateStart) {
        this.drawCelebration(rect, timeMs - this.celebrateStart);
        effects += 1;
      }
      this.metricsValue = {
        entities: frame2.length,
        interpolated: this.tracks.length,
        effects,
        milliseconds: now() - started
      };
    }
    /** Clears the canvas, releases the pointer listeners, so nothing it drew or held outlives the component. */
    dispose() {
      this.context.clearRect(0, 0, this.cssWidth, this.cssHeight);
      this.canvas.removeEventListener("pointermove", this.pointerListener);
      this.canvas.removeEventListener("pointerleave", this.pointerLeaveListener);
      this.hoveredEntityId = null;
    }
    /**
     * Sizes the backing store to the device pixel ratio and returns the pitch's rectangle in CSS pixels.
     *
     * Geometry stays in normalized coordinates and only the transform changes, so the same highlight is drawn
     * the same way on a 1x and a 3x screen (`§9.4`).
     */
    resize() {
      const cssWidth = this.canvas.clientWidth || this.canvas.width || 640;
      const cssHeight = this.canvas.clientHeight || this.canvas.height || 416;
      const ratio = typeof window === "undefined" ? 1 : window.devicePixelRatio || 1;
      const pixelWidth = Math.max(1, Math.round(cssWidth * ratio));
      const pixelHeight = Math.max(1, Math.round(cssHeight * ratio));
      if (this.canvas.width !== pixelWidth || this.canvas.height !== pixelHeight) {
        this.canvas.width = pixelWidth;
        this.canvas.height = pixelHeight;
      }
      this.context.setTransform(ratio, 0, 0, ratio, 0, 0);
      this.cssWidth = cssWidth;
      this.cssHeight = cssHeight;
      return pitchRect(cssWidth, cssHeight);
    }
    /** Draws the pitch: mown stripes, every law-of-the-game marking, and both goals with their nets. */
    drawPitch(rect) {
      const context = this.context;
      const geometry = pitchGeometry(rect);
      context.save();
      context.fillStyle = GRASS_DARK;
      context.fillRect(rect.x, rect.y, rect.width, rect.height);
      context.fillStyle = GRASS_LIGHT;
      for (let stripe = 0; stripe < geometry.stripeCount; stripe += 1) {
        if (stripe % 2 === 0) {
          continue;
        }
        context.fillRect(
          rect.x + stripe * geometry.stripeWidth,
          rect.y,
          geometry.stripeWidth,
          rect.height
        );
      }
      context.strokeStyle = LINE_COLOUR;
      context.lineWidth = geometry.lineWidth;
      context.strokeRect(rect.x, rect.y, rect.width, rect.height);
      context.beginPath();
      context.moveTo(geometry.centreX, rect.y);
      context.lineTo(geometry.centreX, rect.y + rect.height);
      context.stroke();
      context.beginPath();
      context.arc(geometry.centreX, geometry.centreY, geometry.centreCircleRadius, 0, Math.PI * 2);
      context.stroke();
      context.fillStyle = LINE_COLOUR;
      context.beginPath();
      context.arc(geometry.centreX, geometry.centreY, geometry.centreSpotRadius, 0, Math.PI * 2);
      context.fill();
      this.drawEndMarkings(rect, geometry, true);
      this.drawEndMarkings(rect, geometry, false);
      const corners = [
        [rect.x, rect.y, 0, Math.PI / 2],
        [rect.x + rect.width, rect.y, Math.PI / 2, Math.PI],
        [rect.x + rect.width, rect.y + rect.height, Math.PI, Math.PI * 1.5],
        [rect.x, rect.y + rect.height, Math.PI * 1.5, Math.PI * 2]
      ];
      for (const [cornerX, cornerY, from, to] of corners) {
        context.beginPath();
        context.arc(cornerX, cornerY, geometry.cornerArcRadius, from, to);
        context.stroke();
      }
      this.drawGoal(rect, geometry, true);
      this.drawGoal(rect, geometry, false);
      context.restore();
    }
    /** Draws one end's penalty area, six-yard box, penalty spot, and the penalty arc outside the box. */
    drawEndMarkings(rect, geometry, isLeft) {
      const context = this.context;
      const boxX = isLeft ? rect.x : rect.x + rect.width - geometry.penaltyBoxDepth;
      const boxY = geometry.centreY - geometry.penaltyBoxHeight / 2;
      const sixYardX = isLeft ? rect.x : rect.x + rect.width - geometry.sixYardBoxDepth;
      const sixYardY = geometry.centreY - geometry.sixYardBoxHeight / 2;
      const spotX = isLeft ? rect.x + geometry.penaltySpotInset : rect.x + rect.width - geometry.penaltySpotInset;
      context.strokeRect(boxX, boxY, geometry.penaltyBoxDepth, geometry.penaltyBoxHeight);
      context.strokeRect(sixYardX, sixYardY, geometry.sixYardBoxDepth, geometry.sixYardBoxHeight);
      context.fillStyle = LINE_COLOUR;
      context.beginPath();
      context.arc(spotX, geometry.centreY, geometry.penaltySpotRadius, 0, Math.PI * 2);
      context.fill();
      const exit = (geometry.penaltyBoxDepth - geometry.penaltySpotInset) / geometry.centreCircleRadius;
      if (Math.abs(exit) < 1) {
        const angle = Math.acos(exit);
        context.beginPath();
        if (isLeft) {
          context.arc(spotX, geometry.centreY, geometry.centreCircleRadius, -angle, angle);
        } else {
          context.arc(
            spotX,
            geometry.centreY,
            geometry.centreCircleRadius,
            Math.PI - angle,
            Math.PI + angle
          );
        }
        context.stroke();
      }
    }
    /** Draws one goal and its net, outside the goal line. */
    drawGoal(rect, geometry, isLeft) {
      const context = this.context;
      const top = geometry.centreY - geometry.goalWidth / 2;
      const x = isLeft ? rect.x - geometry.goalDepth : rect.x + rect.width;
      const lineWidth = geometry.lineWidth;
      context.save();
      context.fillStyle = "rgba(255, 255, 255, 0.14)";
      context.fillRect(x, top, geometry.goalDepth, geometry.goalWidth);
      context.strokeStyle = "rgba(255, 255, 255, 0.3)";
      context.lineWidth = lineWidth * 0.5;
      context.beginPath();
      for (let strand = 1; strand < 5; strand += 1) {
        const strandX = x + geometry.goalDepth * strand / 5;
        const strandY = top + geometry.goalWidth * strand / 5;
        context.moveTo(strandX, top);
        context.lineTo(strandX, top + geometry.goalWidth);
        context.moveTo(x, strandY);
        context.lineTo(x + geometry.goalDepth, strandY);
      }
      context.stroke();
      context.strokeStyle = "rgba(255, 255, 255, 0.9)";
      context.lineWidth = Math.max(1.5, lineWidth * 1.4);
      context.strokeRect(x, top, geometry.goalDepth, geometry.goalWidth);
      context.restore();
    }
    /** Draws one player: kit colours, a contrasting border, the shirt number, and any card they carry. */
    drawPlayer(rect, item, radius) {
      const context = this.context;
      const point = toCanvasPoint(item.position, rect);
      const isHome = item.entity.side !== "away";
      const kit = isHome ? this.kits.home : this.kits.away;
      const isKeeper = item.entity.family === "goalkeeper";
      const fill = isKeeper ? kit.secondary : kit.primary;
      const border = isKeeper ? kit.primary : "#ffffff";
      context.save();
      context.fillStyle = fill;
      context.strokeStyle = border;
      context.lineWidth = 2;
      if (isHome) {
        context.beginPath();
        context.arc(point.x, point.y, radius, 0, Math.PI * 2);
        context.fill();
        context.stroke();
      } else {
        context.fillRect(point.x - radius, point.y - radius, radius * 2, radius * 2);
        context.strokeRect(point.x - radius, point.y - radius, radius * 2, radius * 2);
      }
      context.fillStyle = readableInk(fill);
      context.font = `600 ${Math.max(7, Math.round(radius * 1.15))}px system-ui, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText(`${item.entity.shirtNumber}`, point.x, point.y + 0.5);
      context.restore();
      const card = item.entity.participantId === null ? void 0 : this.cards.get(item.entity.participantId);
      if (card !== void 0) {
        this.drawCard(point, radius, card);
      }
    }
    /** Draws a booking as a small card floating above the offending player's token. */
    drawCard(point, radius, card) {
      const context = this.context;
      const width = Math.max(5, radius * 0.7);
      const height = width * 1.4;
      const x = point.x + radius * 0.7;
      const y = point.y - radius - height - 2;
      context.save();
      context.fillStyle = CARD_COLOURS[card];
      context.strokeStyle = "rgba(15, 23, 42, 0.9)";
      context.lineWidth = 1;
      context.fillRect(x, y, width, height);
      context.strokeRect(x, y, width, height);
      context.restore();
    }
    /** Draws a ripple where two players are contesting the ball. */
    drawClash(rect, x, y, phase) {
      const context = this.context;
      const point = toCanvasPoint({ x, y }, rect);
      const cycle = (this.lastTimeMs + phase) % CLASH_PERIOD / CLASH_PERIOD;
      const radius = 5 + cycle * 9;
      context.save();
      context.strokeStyle = `rgba(255, 255, 255, ${(0.55 * (1 - cycle)).toFixed(3)})`;
      context.lineWidth = 1.5;
      context.beginPath();
      context.arc(point.x, point.y, radius, 0, Math.PI * 2);
      context.stroke();
      context.restore();
    }
    /** Draws the ball at its altitude, above its own shadow, with the aerial trail behind it. */
    drawBall(rect, ball) {
      const context = this.context;
      const ground = toCanvasPoint(ball.position, rect);
      const lift = altitudeLift(rect, ball.z);
      const radius = ballRadius(rect) * altitudeScale(ball.z);
      const centre = { x: ground.x, y: ground.y - lift };
      context.save();
      context.fillStyle = `rgba(2, 6, 23, ${(0.35 - ball.z / 100 * 0.15).toFixed(3)})`;
      context.beginPath();
      context.ellipse(
        ground.x,
        ground.y,
        radius * (1.1 - ball.z / 100 * 0.35),
        radius * 0.42,
        0,
        0,
        Math.PI * 2
      );
      context.fill();
      context.restore();
      context.save();
      context.beginPath();
      context.arc(centre.x, centre.y, radius, 0, Math.PI * 2);
      context.fillStyle = BALL_COLOUR;
      context.fill();
      context.clip();
      context.fillStyle = BALL_PATCH_COLOUR;
      context.beginPath();
      for (let corner = 0; corner < 5; corner += 1) {
        const angle = -Math.PI / 2 + corner * Math.PI * 2 / 5;
        const x = centre.x + Math.cos(angle) * radius * 0.34;
        const y = centre.y + Math.sin(angle) * radius * 0.34;
        if (corner === 0) {
          context.moveTo(x, y);
        } else {
          context.lineTo(x, y);
        }
      }
      context.closePath();
      context.fill();
      for (const angle of [0, Math.PI / 2, Math.PI, Math.PI * 1.5]) {
        context.lineWidth = Math.max(0.8, radius * 0.16);
        context.strokeStyle = BALL_PATCH_COLOUR;
        context.beginPath();
        context.moveTo(
          centre.x + Math.cos(angle) * radius * 0.42,
          centre.y + Math.sin(angle) * radius * 0.42
        );
        context.lineTo(centre.x + Math.cos(angle) * radius, centre.y + Math.sin(angle) * radius);
        context.stroke();
      }
      context.restore();
      context.save();
      context.beginPath();
      context.arc(centre.x, centre.y, radius, 0, Math.PI * 2);
      context.strokeStyle = BALL_PATCH_COLOUR;
      context.lineWidth = 1.2;
      context.stroke();
      context.restore();
    }
    /** Draws the faint path a fast-moving or airborne ball has just travelled. */
    drawTrail(rect, strength) {
      const context = this.context;
      const span = 380;
      const steps = 6;
      context.save();
      for (let step = steps; step >= 2; step -= 1) {
        const from = sampleAt(this.ballTrack ?? [], this.lastTimeMs - span * step / steps);
        const to = sampleAt(this.ballTrack ?? [], this.lastTimeMs - span * (step - 1) / steps);
        if (from === null || to === null) {
          continue;
        }
        const alpha = strength * 0.45 * (1 - step / (steps + 1));
        context.strokeStyle = `rgba(248, 250, 252, ${alpha.toFixed(3)})`;
        context.lineWidth = 1.5;
        context.beginPath();
        context.moveTo(
          toCanvasPoint(from, rect).x,
          toCanvasPoint(from, rect).y - altitudeLift(rect, from.z)
        );
        context.lineTo(
          toCanvasPoint(to, rect).x,
          toCanvasPoint(to, rect).y - altitudeLift(rect, to.z)
        );
        context.stroke();
      }
      context.restore();
    }
    /** Draws the bright projectile streak a struck ball leaves behind it. */
    drawShotLine(rect, ball, timeMs) {
      const context = this.context;
      const from = sampleAt(this.ballTrack ?? [], timeMs - 240);
      if (from === null) {
        return;
      }
      const start = toCanvasPoint(from, rect);
      const end = toCanvasPoint(ball.position, rect);
      const endY = end.y - altitudeLift(rect, ball.z);
      const startY = start.y - altitudeLift(rect, from.z);
      context.save();
      context.strokeStyle = "rgba(253, 224, 71, 0.85)";
      context.lineWidth = 2;
      context.beginPath();
      context.moveTo(start.x, startY);
      context.lineTo(end.x, endY);
      context.stroke();
      context.fillStyle = "rgba(253, 224, 71, 0.9)";
      context.beginPath();
      context.arc(end.x, endY, 2.4, 0, Math.PI * 2);
      context.fill();
      context.restore();
    }
    /** Draws a name tag above the player the ball is at. */
    drawNameTag(rect, item, radius) {
      const name = item.entity.name;
      if (name === null || name.length === 0) {
        return;
      }
      const point = toCanvasPoint(item.position, rect);
      this.drawTag(point, radius, name);
    }
    /** Draws the pointer's own highlight and name tag over a player. */
    drawHover(rect, item, radius) {
      const context = this.context;
      const point = toCanvasPoint(item.position, rect);
      context.save();
      context.strokeStyle = "rgba(250, 204, 21, 0.9)";
      context.lineWidth = 2;
      context.beginPath();
      context.arc(point.x, point.y, radius + 3, 0, Math.PI * 2);
      context.stroke();
      context.restore();
      if (item.entity.name !== null && item.entity.name.length > 0) {
        this.drawTag(point, radius + 3, item.entity.name);
      }
    }
    /** Draws a player's name in a dark pill just above their token. */
    drawTag(point, radius, name) {
      const context = this.context;
      const label = name.length > 18 ? `${name.slice(0, 17)}\u2026` : name;
      const width = label.length * 5.2 + 10;
      const height = 13;
      const x = point.x - width / 2;
      const y = point.y - radius - height - 6;
      context.save();
      context.fillStyle = TAG_BACKGROUND;
      context.fillRect(x, y, width, height);
      context.strokeStyle = TAG_BORDER;
      context.lineWidth = 1;
      context.strokeRect(x, y, width, height);
      context.fillStyle = "#f8fafc";
      context.font = "600 9px system-ui, sans-serif";
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText(label, point.x, y + height / 2 + 0.5);
      context.restore();
    }
    /** Draws the goal celebration: a pulsating flash and the GOAL! badge. */
    drawCelebration(rect, elapsed) {
      const fade = Math.max(0, 1 - elapsed / CELEBRATION_MILLISECONDS);
      if (fade <= 0) {
        return;
      }
      const context = this.context;
      const pulse = this.reducedMotion ? 1 : 0.55 + 0.45 * Math.sin(elapsed / 90);
      const size = this.reducedMotion ? 42 : 38 + 8 * pulse;
      const x = rect.x + rect.width / 2;
      const y = rect.y + rect.height * 0.3;
      const width = size * 3.4;
      const height = size * 1.5;
      context.save();
      context.fillStyle = `rgba(255, 255, 255, ${(0.13 * fade * pulse).toFixed(3)})`;
      context.fillRect(0, 0, this.cssWidth, this.cssHeight);
      context.fillStyle = `rgba(5, 46, 22, ${(0.88 * fade).toFixed(3)})`;
      context.fillRect(x - width / 2, y - height / 2, width, height);
      context.strokeStyle = `rgba(52, 211, 153, ${(0.95 * fade).toFixed(3)})`;
      context.lineWidth = 2;
      context.strokeRect(x - width / 2, y - height / 2, width, height);
      context.fillStyle = `rgba(167, 243, 208, ${fade.toFixed(3)})`;
      context.font = `900 ${Math.round(size)}px system-ui, sans-serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillText("GOAL!", x, y + size * 0.05);
      context.restore();
    }
    /** Whether any player or the ball itself is tagged as striking the ball at goal. */
    isStriking(ball, players) {
      return isStrikeAction(ball.action) || players.some((player) => isStrikeAction(player.action));
    }
    /** Finds the entity under the pointer, so hovering raises a name. */
    pointerListener = (event) => {
      if (this.tracks.length === 0) {
        return;
      }
      const bounds = this.canvas.getBoundingClientRect();
      const pointer = { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
      const rect = pitchRect(this.cssWidth || 640, this.cssHeight || 416);
      const radius = playerRadius(rect) + 4;
      let hovered = null;
      for (const item of frameAt(this.entities, this.tracks, this.lastTimeMs)) {
        const point = toCanvasPoint(item.position, rect);
        if (Math.hypot(point.x - pointer.x, point.y - pointer.y) <= radius) {
          hovered = item.entity.id;
          break;
        }
      }
      if (hovered !== this.hoveredEntityId) {
        this.hoveredEntityId = hovered;
        this.render(this.lastTimeMs);
      }
    };
    pointerLeaveListener = () => {
      if (this.hoveredEntityId !== null) {
        this.hoveredEntityId = null;
        this.render(this.lastTimeMs);
      }
    };
  };
  var BALL_ENTITY_ID = "ball";
  var GRASS_DARK = "#15632f";
  var GRASS_LIGHT = "#1a7d3c";
  var LINE_COLOUR = "rgba(255, 255, 255, 0.78)";
  var BALL_COLOUR = "#f8fafc";
  var BALL_PATCH_COLOUR = "#111827";
  var TAG_BACKGROUND = "rgba(2, 6, 23, 0.85)";
  var TAG_BORDER = "rgba(248, 250, 252, 0.3)";
  var CARD_COLOURS = { yellow: "#facc15", red: "#ef4444" };
  var CLASH_PERIOD = 620;
  var CELEBRATION_MILLISECONDS = 4e3;
  var TRAIL_MIN_STRENGTH = 0.34;
  var DEFAULT_KEEPER_TRIM = "#f8fafc";
  function toRendererEntities(entities2) {
    return entities2.map((entity) => ({
      id: entity.entityId,
      isBall: entity.isBall,
      side: entity.side === "home" || entity.side === "away" ? entity.side : null,
      participantId: entity.participantId ?? null,
      shirtNumber: entity.shirtNumber,
      family: entity.family,
      name: entity.name ?? null,
      anchor: { x: entity.x, y: entity.y }
    }));
  }
  function toRendererTracks(tracks2) {
    return tracks2.filter((track2) => track2.keyframes.length > 0).map((track2) => ({ entityId: track2.entityId, keyframes: track2.keyframes }));
  }
  function readableInk(hex) {
    const value = hex.replace("#", "");
    const full = value.length === 3 ? value.split("").map((character) => character + character).join("") : value;
    if (full.length !== 6) {
      return "#ffffff";
    }
    const red = Number.parseInt(full.slice(0, 2), 16);
    const green = Number.parseInt(full.slice(2, 4), 16);
    const blue = Number.parseInt(full.slice(4, 6), 16);
    if (Number.isNaN(red) || Number.isNaN(green) || Number.isNaN(blue)) {
      return "#ffffff";
    }
    const luminance = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255;
    return luminance > 0.6 ? "#0f172a" : "#ffffff";
  }
  function playerRadius(rect) {
    return Math.max(5, Math.min(10, rect.height / 58));
  }
  function ballRadius(rect) {
    return Math.max(3.5, Math.min(6.5, rect.height / 105));
  }
  function kitFor(kit, primary) {
    return {
      primary: kit?.primary ?? primary,
      secondary: kit?.secondary ?? DEFAULT_KEEPER_TRIM
    };
  }
  function now() {
    return typeof performance === "undefined" ? 0 : performance.now();
  }

  // .preview/stage6-demo.ts
  function slot(entityId, side, shirt, family, name, x, y) {
    return {
      entityId,
      isBall: false,
      side,
      participantId: entityId,
      shirtNumber: shirt,
      family,
      x,
      y,
      name
    };
  }
  function shape(side, offset) {
    const prefix = side === "home" ? "H" : "A";
    return [
      slot(`${prefix}1`, side, 1, "goalkeeper", `${side} keeper`, side === "home" ? 500 : 9500, 5e3),
      slot(`${prefix}2`, side, 2, "defence", `${side} right back`, side === "home" ? 2500 + offset : 7500 + offset, 1800),
      slot(`${prefix}5`, side, 5, "defence", `${side} centre back`, side === "home" ? 2400 + offset : 7600 + offset, 3600),
      slot(`${prefix}6`, side, 6, "defence", `${side} centre back`, side === "home" ? 2400 + offset : 7600 + offset, 6400),
      slot(`${prefix}3`, side, 3, "defence", `${side} left back`, side === "home" ? 2500 + offset : 7500 + offset, 8200),
      slot(`${prefix}7`, side, 7, "midfield", `${side} winger`, side === "home" ? 4600 : 5400, 1600),
      slot(`${prefix}8`, side, 8, "midfield", `${side} engine`, side === "home" ? 4400 : 5600, 4400),
      slot(`${prefix}4`, side, 4, "midfield", `${side} holder`, side === "home" ? 4400 : 5600, 5600),
      slot(`${prefix}11`, side, 11, "midfield", `${side} winger`, side === "home" ? 4600 : 5400, 8400),
      slot(`${prefix}10`, side, 10, "attack", `${side} ten`, side === "home" ? 6400 : 3600, 5e3),
      slot(`${prefix}9`, side, 9, "attack", `${side} nine`, side === "home" ? 6600 : 3400, 3400)
    ];
  }
  function track(entityId, keyframes) {
    return { entityId, keyframes };
  }
  var duration = 2e4;
  var strike = 11e3;
  var home = shape("home", 0);
  var away = shape("away", 0);
  var entities = [
    ...home,
    ...away,
    { entityId: "ball", isBall: true, side: null, participantId: null, shirtNumber: 0, family: null, x: 7400, y: 5e3 }
  ];
  var tracks = [
    // The shape shifts with the play, home pushing on and the away block dropping off.
    ...home.map(
      (entity) => track(entity.entityId, [
        { timeMilliseconds: 0, x: entity.x, y: entity.y },
        { timeMilliseconds: duration * 2 / 3, x: Math.min(9e3, entity.x + 500), y: entity.y + (5e3 - entity.y) / 8 },
        { timeMilliseconds: duration, x: Math.min(9e3, entity.x + 500), y: entity.y + (5e3 - entity.y) / 8 }
      ])
    ),
    ...away.map(
      (entity) => track(entity.entityId, [
        { timeMilliseconds: 0, x: entity.x, y: entity.y },
        { timeMilliseconds: duration * 2 / 3, x: Math.max(1e3, entity.x - 400), y: entity.y + (5e3 - entity.y) / 6 },
        { timeMilliseconds: duration, x: Math.max(1e3, entity.x - 400), y: entity.y + (5e3 - entity.y) / 6 }
      ])
    ),
    // The striker: start, close the ball, strike it, celebrate.
    track("H9", [
      { timeMilliseconds: 0, x: 6600, y: 3400 },
      { timeMilliseconds: strike - 1200, x: 7100, y: 4300, action: "run" },
      { timeMilliseconds: strike, x: 7400, y: 5e3, action: "shot" },
      { timeMilliseconds: duration, x: 7800, y: 5400, action: "celebrate" }
    ]),
    // A defender closes him down, which is the duel the clash ring marks.
    track("A6", [
      { timeMilliseconds: 0, x: 7600, y: 6400 },
      { timeMilliseconds: strike, x: 7580, y: 5120, action: "tackle" },
      { timeMilliseconds: duration, x: 8200, y: 5600 }
    ]),
    // The keeper: hold, come for the ball's line, recover.
    track("A1", [
      { timeMilliseconds: 0, x: 9500, y: 5e3 },
      { timeMilliseconds: duration * 2 / 3, x: 9200, y: 5e3, action: "save" },
      { timeMilliseconds: duration, x: 9500, y: 5e3 }
    ]),
    // The ball: a build-up pass into the striker, struck at goal, climbing to 55.
    track("ball", [
      { timeMilliseconds: 0, x: 4e3, y: 2400, z: 0, speed: 1800 },
      { timeMilliseconds: strike - 1e3, x: 7200, y: 4800, z: 6, speed: 2600 },
      { timeMilliseconds: strike, x: 7400, y: 5e3, z: 12, speed: 4600 },
      { timeMilliseconds: strike + 900, x: 9300, y: 5050, z: 55, speed: 4200 },
      { timeMilliseconds: strike + 1800, x: 1e4, y: 5e3, z: 0, speed: 1500 },
      { timeMilliseconds: duration, x: 1e4, y: 5e3, z: 0 }
    ])
  ];
  var highlight = {
    sourceEventSequence: 42,
    minute: 67,
    stoppageMinute: 0,
    durationMilliseconds: duration,
    outcomeCode: "goal",
    narration: "Goal \u2014 H nine, 67.",
    homeColour: "#1f4e79",
    awayColour: "#8c2f39",
    entities,
    tracks,
    commentary: [
      {
        timeMilliseconds: 0,
        templateKey: "match.passage.build_up",
        variantKey: "match.passage.build_up.v1",
        parameters: [],
        text: "The move is built."
      },
      {
        timeMilliseconds: strike,
        templateKey: "match.passage.shot",
        variantKey: "match.passage.shot.v1",
        parameters: [],
        text: "The shot is struck."
      },
      {
        timeMilliseconds: strike + 900,
        templateKey: "match.goal",
        variantKey: "match.goal.v1",
        parameters: [],
        text: "Goal!"
      }
    ]
  };
  var canvas = document.getElementById("pitch");
  var fpsLabel = document.getElementById("fps");
  var timeLabel = document.getElementById("time");
  var renderer = new CanvasMatchRenderer(canvas, highlight, {
    kits: {
      home: { primary: "#1f4e79", secondary: "#d6e4f0" },
      away: { primary: "#8c2f39", secondary: "#f2e3e5" }
    },
    cards: /* @__PURE__ */ new Map([["A6", "yellow"]])
  });
  var params = new URLSearchParams(location.search);
  var position = Number(params.get("t") ?? 0);
  var playing = params.get("paused") !== "1";
  var speed = Number(params.get("speed") ?? 1);
  var previous = performance.now();
  var frames = 0;
  var reportedAt = previous;
  var worst = 0;
  function frame(now2) {
    const delta = playing ? now2 - previous : 0;
    previous = now2;
    position = position + delta * speed;
    if (position >= duration) {
      position = 0;
    }
    renderer.render(position);
    const cost = renderer.metrics.milliseconds;
    worst = Math.max(worst, cost);
    frames += 1;
    if (now2 - reportedAt >= 500) {
      fpsLabel.textContent = `${Math.round(frames * 1e3 / (now2 - reportedAt))} fps`;
      timeLabel.textContent = `${(position / 1e3).toFixed(1)}s of ${duration / 1e3}s \u2014 render ${cost.toFixed(1)} ms, worst ${worst.toFixed(1)} ms`;
      frames = 0;
      reportedAt = now2;
      worst = 0;
    }
    requestAnimationFrame(frame);
  }
  document.getElementById("play")?.addEventListener("click", () => {
    playing = !playing;
  });
  document.querySelectorAll("button[data-speed]").forEach((button) => {
    button.addEventListener("click", () => {
      speed = Number(button.dataset.speed);
    });
  });
  window.stage6 = {
    render: (at) => {
      position = at;
      playing = false;
      renderer.render(at);
    },
    metrics: () => renderer.metrics
  };
  requestAnimationFrame(frame);
})();
