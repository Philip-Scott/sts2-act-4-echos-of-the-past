// Throwaway study: can articulated collet jaws make the armillary read as a workshop restraint?
(function () {
  "use strict";

  const TAU = Math.PI * 2;
  const INK = "#302937", BRASS = "#b59961", LIGHT = "#dfd1ac";
  const COPPER = "#76654b", PATINA = "#58776d", LILAC = "#bfacd7", BLUE = "#83b6df";
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

  function point(r, a, scale = 1) {
    const x = Math.cos(a), y = Math.sin(a);
    return [
      r.x + scale * (r.u[0] * x + r.v[0] * y),
      r.y + scale * (r.u[1] * x + r.v[1] * y),
      r.u[2] * x + r.v[2] * y
    ];
  }

  function arc(r, start, end, scale = 1, count = 18) {
    return Array.from({ length: count + 1 }, (_, i) => point(r, start + (end - start) * i / count, scale));
  }

  // Interpolate crossings so a plate, pipe or afterimage ends at the same depth on both passes.
  function depthRuns(points, front) {
    const runs = [];
    let run = [];
    for (let i = 0; i < points.length; i++) {
      const p = points[i], visible = (p[2] >= 0) === front;
      if (i) {
        const previous = points[i - 1];
        if (((previous[2] >= 0) === front) !== visible) {
          const fraction = previous[2] / (previous[2] - p[2]);
          const crossing = p.map((v, j) => previous[j] + (v - previous[j]) * fraction);
          if (visible) run = [crossing];
          else {
            run.push(crossing);
            if (run.length > 1) runs.push(run);
            run = [];
          }
        }
      }
      if (visible) run.push(p);
    }
    if (run.length > 1) runs.push(run);
    return runs;
  }

  function path(c, points, close = false) {
    c.beginPath();
    points.forEach((p, i) => i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]));
    if (close) c.closePath();
  }

  function line(c, points, color, width, alpha) {
    path(c, points);
    c.strokeStyle = color;
    c.lineWidth = width;
    c.globalAlpha = alpha;
    c.stroke();
  }

  function wire(c, points, front, color, width, alpha) {
    for (const run of depthRuns(points, front)) line(c, run, color, width, alpha);
  }

  function rivet(c, p, radius, alpha) {
    c.globalAlpha = alpha;
    c.fillStyle = INK;
    c.beginPath(); c.arc(p[0], p[1], radius * 1.35, 0, TAU); c.fill();
    c.fillStyle = BRASS;
    c.beginPath(); c.arc(p[0], p[1], radius, 0, TAU); c.fill();
    line(c, [[p[0] - radius * .45, p[1] - radius * .3], [p[0] + radius * .4, p[1] - radius * .3]],
      LIGHT, Math.max(.6, radius * .4), alpha * .7);
  }

  function metal(c, x, y, radius) {
    const gradient = c.createLinearGradient(x - radius, y - radius * .35, x + radius, y + radius * .35);
    gradient.addColorStop(0, "#445c56");
    gradient.addColorStop(.26, PATINA);
    gradient.addColorStop(.48, "#baa16d");
    gradient.addColorStop(.73, COPPER);
    gradient.addColorStop(1, "#495e59");
    return gradient;
  }

  // Screen-space bevel width keeps the flattened front of a jaw legible at combat scale.
  function plate(c, r, a0, a1, scale, width, front, alpha) {
    const samples = [];
    for (let i = 0; i <= 12; i++) {
      const f = i / 12, a = a0 + (a1 - a0) * f, p = point(r, a, scale);
      const dx = -r.u[0] * Math.sin(a) + r.v[0] * Math.cos(a);
      const dy = -r.u[1] * Math.sin(a) + r.v[1] * Math.cos(a);
      const length = Math.hypot(dx, dy) || 1;
      const outward = dy * (p[0] - r.x) - dx * (p[1] - r.y) >= 0 ? 1 : -1;
      samples.push([...p, dy / length * outward, -dx / length * outward,
        width * (.48 + .52 * Math.min(1, f * 5, (1 - f) * 5))]);
    }
    for (const run of depthRuns(samples, front)) {
      const outer = run.map(p => [p[0] + p[3] * p[5], p[1] + p[4] * p[5]]);
      const inner = run.map(p => [p[0] - p[3] * p[5], p[1] - p[4] * p[5]]);
      path(c, outer.concat(inner.slice().reverse()), true);
      c.globalAlpha = alpha;
      c.fillStyle = metal(c, r.x, r.y, Math.hypot(r.u[0], r.u[1]));
      c.fill();
      c.strokeStyle = INK; c.lineWidth = Math.max(.8, width * .38); c.stroke();
      line(c, outer, LIGHT, Math.max(.65, width * .22), alpha * .65);
      line(c, inner, "#344d4a", Math.max(.8, width * .35), alpha * .9);
    }
  }

  function vessel(c, p, size, alpha, pressure) {
    c.save();
    c.translate(p[0], p[1]);
    const shape = [[0, -size * 2.1], [size, -size * .9], [size * .8, size * 1.1],
      [0, size * 1.7], [-size * .8, size * 1.1], [-size, -size * .9]];
    path(c, shape, true);
    c.globalAlpha = alpha; c.fillStyle = "#37334d"; c.fill();
    c.strokeStyle = BRASS; c.lineWidth = size * .36; c.stroke();
    path(c, [[0, -size * 1.55], [size * .42, -.2 * size], [0, size],
      [-size * .4, .25 * size]], true);
    c.globalAlpha = alpha * (.5 + pressure * .28); c.fillStyle = LILAC; c.fill();
    line(c, [[-.25 * size, -size], [-.32 * size, size * .2]], BLUE, size * .3, alpha * .68);
    line(c, [[-size, -size], [0, -size * 1.65], [size, -size]], LIGHT, size * .42, alpha * .8);
    line(c, [[-size * .8, size], [size * .8, size]], COPPER, size * .5, alpha);
    c.restore();
  }

  function ground(c, base, cx, cy, rx, ry, w, time, pressure) {
    const ring = { x: cx, y: cy, u: [rx, 0, 0], v: [0, ry, 0] };
    c.save();
    c.translate(cx, cy); c.scale(rx, ry);
    const pool = c.createRadialGradient(0, 0, .05, 0, 0, 1);
    pool.addColorStop(0, "rgba(83,62,106,.08)");
    pool.addColorStop(.6, "rgba(84,65,110,.32)");
    pool.addColorStop(1, "rgba(48,38,61,0)");
    c.globalAlpha = base; c.fillStyle = pool;
    c.beginPath(); c.arc(0, 0, 1, 0, TAU); c.fill();
    c.restore();

    line(c, arc(ring, 0, TAU, .77, 64), LILAC, w * .36, base * .38);
    line(c, arc(ring, 0, TAU, .52, 64), LIGHT, w * .3, base * .24);
    // Drafter's construction lines and perpendicular end marks, not a rune alphabet.
    for (const a of [.27, 1.72]) {
      const p = point(ring, a, .72), q = point(ring, a + Math.PI, .72);
      line(c, [p, q], LIGHT, w * .3, base * .25);
      for (const b of [a, a + Math.PI]) {
        line(c, [point(ring, b - .045, .79), point(ring, b + .045, .79)], LIGHT, w * .45, base * .45);
      }
    }
    for (let i = 0; i < 6; i++) {
      const a = i * TAU / 6;
      line(c, arc(ring, a + .08, a + TAU / 6 - .08), INK, w * 2.4, base * .75);
      line(c, arc(ring, a + .08, a + TAU / 6 - .08), metal(c, cx, cy, rx), w * 1.4, base * .85);
      line(c, [point(ring, a, .8), point(ring, a, .94)], LILAC, w * .65, base * (.32 + pressure * .35));
    }
    for (let i = 0; i < 2; i++) {
      const phase = ((time / 6 + i / 2) % 1 + 1) % 1;
      const strength = Math.sin(Math.PI * phase) * .28;
      line(c, arc(ring, 0, TAU, .3 + phase * .65, 64), LILAC, w * .6, base * strength);
    }
  }

  function draw(context, bounds, time, opacity, front) {
    if (!(opacity > 0) || !(context.globalAlpha > 0)) return;
    const c = context, [left, top, width, height] = bounds, t = time || 0;
    const cx = left + width * .5, feet = top + height;
    const w = clamp(height * .0068, 1.5, 2.6), rx = width * .72;
    const floorDepth = clamp(height * .04, 9, 14), floorY = feet - floorDepth - w * 2.5;
    const waist = top + height * .57, pressure = .5 - .5 * Math.cos(t * .62);
    const lean = .035 * Math.sin(t * .19);
    const collar = { x: cx, y: waist,
      u: [rx, rx * lean, 0], v: [-height * .13 * lean, height * .13, 1] };
    const meridian = { x: cx, y: top + height * .5,
      u: [width * .575, 0, .09 * Math.sin(t * .16)], v: [0, -height * .465, -.85] };

    c.save();
    const base = c.globalAlpha * Math.min(1, opacity);
    c.globalAlpha = base; c.globalCompositeOperation = "source-over";
    c.lineJoin = "round"; c.lineCap = "round"; c.setLineDash([]);
    c.shadowBlur = 0; c.shadowOffsetX = 0; c.shadowOffsetY = 0;

    if (!front) ground(c, base, cx, floorY, rx * 1.04, floorDepth, w, t, pressure);

    // Two bent supply pipes tie the floor sockets, side bearings and upright bow together.
    for (const side of [-1, 1]) {
      const socket = [cx + side * rx * .86, floorY, -.55];
      const elbow = [cx + side * rx * 1.025, floorY - height * .13, -.45];
      const bearing = [cx + side * rx * 1.025, waist + height * .025, -.18];
      const inset = [cx + side * width * .575, waist - height * .07, -.12];
      const pipe = [socket, elbow, bearing, inset];
      wire(c, pipe, front, INK, w * 3.1, base * .65);
      wire(c, pipe, front, COPPER, w * 1.95, base * .82);
      wire(c, pipe.map(p => [p[0] - w * .3, p[1] - w * .3, p[2]]), front, BRASS, w * .5, base * .7);
      if (!front) {
        rivet(c, socket, w * 1.75, base * .9);
        rivet(c, bearing, w * 2.1, base * .9);
        vessel(c, [elbow[0], elbow[1] - height * .055], w * 1.8, base * .9, pressure);
      }
    }

    const strength = base * (front ? .8 : .55);
    // The bow is bolted into the stand. Its upper half always remains behind the head.
    for (let i = 0; i < 8; i++) {
      const a = i * TAU / 8;
      plate(c, meridian, a + .035, a + TAU / 8 - .035, 1, w * .68, front, strength);
      const joint = point(meridian, a);
      if ((joint[2] >= 0) === front) rivet(c, joint, w * .7, strength);
    }
    for (let i = 1; i <= 2; i++) {
      const scale = 1 + .017 * i + pressure * .012;
      wire(c, arc(meridian, 0, TAU, scale, 80), front, LILAC, w * .36, base * (.15 - i * .04));
    }

    const cam = t * .075;
    // Opposite jaw pairs share a cam stroke. Short exposed pistons bridge each open sector.
    for (let i = 0; i < 6; i++) {
      const a = cam + i * TAU / 6;
      const stroke = .5 - .5 * Math.cos(t * .62 - (i % 3) * .18);
      const radius = 1.025 - stroke * .062;
      const a0 = a - .35, a1 = a + .35;
      for (let echo = 2; echo >= 1; echo--) {
        const lag = echo * .38, oldCam = (t - lag) * .075;
        const oldStroke = .5 - .5 * Math.cos((t - lag) * .62 - (i % 3) * .18);
        const oldA = oldCam + i * TAU / 6;
        const ghost = arc(collar, oldA - .35, oldA + .35, 1.025 - oldStroke * .062 + echo * .034);
        wire(c, ghost, front, LILAC, w * .65, base * (front ? .24 : .15) / echo);
      }
      const carriage = arc(collar, a - .22, a + .22, 1.11);
      wire(c, carriage, front, INK, w * 1.9, strength);
      wire(c, carriage, front, BRASS, w * .85, strength);
      for (const offset of [-.19, .19]) {
        const outer = point(collar, a + offset, 1.11);
        const middle = point(collar, a + offset, 1.045);
        const inner = point(collar, a + offset, radius);
        wire(c, [outer, inner], front, INK, w * 2.25, strength);
        wire(c, [outer, middle], front, PATINA, w * 1.65, strength);
        wire(c, [middle, inner], front, LIGHT, w * .75, strength);
      }
      plate(c, collar, a0, a1, radius, w * 1.65, front, base * (front ? .96 : .65));
      for (const end of [a0 + .055, a1 - .055]) {
        const p = point(collar, end, radius);
        if ((p[2] >= 0) === front) rivet(c, p, w * .76, base * (front ? .95 : .55));
      }
      // Captive lilac energy occupies the gaps between jaws, rather than coating the body.
      const gap = arc(collar, a1 + .035, a + TAU / 6 - .385, radius);
      wire(c, gap, front, "#665078", w * 2.1, base * (.18 + stroke * .12));
      wire(c, gap, front, LILAC, w * .55, base * (.35 + stroke * .3));
      const key = point(collar, a, 1.11);
      if ((key[2] >= 0) === front) {
        const s = w * 1.65;
        path(c, [[key[0] - s, key[1]], [key[0] - s * .45, key[1] - s],
          [key[0] + s * .65, key[1] - s * .7], [key[0] + s, key[1] + s * .45],
          [key[0] - s * .6, key[1] + s * .55]], true);
        c.globalAlpha = base * (front ? .85 : .55); c.fillStyle = LIGHT; c.fill();
        c.strokeStyle = COPPER; c.lineWidth = w * .45; c.stroke();
      }
    }
    c.restore();
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies["astra-architect"] = {
    name: "Echo Collet",
    description: "Six brass jaws close in opposing pairs around lilac energy. Bent supply pipes join the upright bow to a chalk-marked floor cradle.",
    fit: "Patinated workshop metal, ivory fittings and small blue-lit energy vessels.",
    tradeoff: "A heavier waist silhouette than the original armillary.",
    implementation: "Depth-clipped plate strips, paired cam strokes and delayed lilac outlines. No body edits or blur.",
    draw
  };
})();
