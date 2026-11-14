(function () {
  "use strict";

  const TAU = Math.PI * 2;
  const INK = "#302735", NAVY = "#30333f", IVORY = "#e7d8b8";
  const CHALK = "#bcabc9", LILAC = "#bba5d6", COPPER = "#a07b57";
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

  function point(r, angle, scale = 1) {
    const x = Math.cos(angle) * scale, y = Math.sin(angle) * scale;
    return [
      r.cx + r.u[0] * x + r.v[0] * y,
      r.cy + r.u[1] * x + r.v[1] * y,
      r.u[2] * x + r.v[2] * y
    ];
  }

  function orbit(r, scale = 1, start = 0, end = TAU) {
    const result = [], steps = Math.max(8, Math.ceil((end - start) / TAU * 112));
    for (let i = 0; i <= steps; i++) result.push(point(r, start + (end - start) * i / steps, scale));
    return result;
  }

  // Clip at the body's depth plane, including the exact crossing in both passes.
  function split(points, front) {
    const result = [];
    let run = null;
    for (let i = 1; i < points.length; i++) {
      const a = points[i - 1], b = points[i];
      const aIn = (a[2] >= 0) === front, bIn = (b[2] >= 0) === front;
      if (aIn && !run) { run = [a]; result.push(run); }
      if (aIn === bIn) {
        if (bIn) run.push(b);
      } else {
        const f = a[2] / (a[2] - b[2]);
        const cross = [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, 0];
        if (aIn) { run.push(cross); run = null; }
        else { run = [cross, b]; result.push(run); }
      }
    }
    return result;
  }

  function paths(c, runs, dy = 0) {
    c.beginPath();
    for (const points of runs) {
      points.forEach((p, i) => i ? c.lineTo(p[0], p[1] + dy) : c.moveTo(p[0], p[1] + dy));
    }
  }

  function polygon(c, points) {
    c.beginPath();
    points.forEach((p, i) => i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]));
    c.closePath();
  }

  function metal(c, x0, y0, x1, y1) {
    const gradient = c.createLinearGradient(x0, y0, x1, y1);
    [[0, "#40574f"], [.23, "#82937b"], [.42, "#c8ac73"], [.63, "#99754c"], [1, "#495a51"]]
      .forEach(([stop, color]) => gradient.addColorStop(stop, color));
    return gradient;
  }

  function brassLine(c, base, points, front, width, strength, paint) {
    const runs = split(points, front);
    paths(c, runs, width * .32);
    c.globalAlpha = base * strength * .7;
    c.lineWidth = width * 1.85; c.strokeStyle = INK; c.stroke();
    paths(c, runs);
    c.globalAlpha = base * strength;
    c.lineWidth = width; c.strokeStyle = paint; c.stroke();
    paths(c, runs, -width * .24);
    c.globalAlpha = base * strength * .52;
    c.lineWidth = Math.max(.55, width * .22); c.strokeStyle = IVORY; c.stroke();
  }

  function band(c, base, r, front, width, strength, graduations) {
    const ex = Math.abs(r.u[0]) + Math.abs(r.v[0]);
    const ey = Math.abs(r.u[1]) + Math.abs(r.v[1]);
    brassLine(c, base, orbit(r), front, width, strength,
      metal(c, r.cx - ex, r.cy - ey, r.cx + ex, r.cy + ey));

    c.globalAlpha = base * strength * .72;
    c.strokeStyle = INK; c.lineWidth = Math.max(.6, width * .17);
    c.beginPath();
    for (let i = 0; i < graduations; i++) {
      const a = i / graduations * TAU, p = point(r, a);
      if ((p[2] >= 0) !== front) continue;
      const q = point(r, a + .002), dx = q[0] - p[0], dy = q[1] - p[1];
      const length = Math.hypot(dx, dy) || 1, tick = width * (i % 4 === 0 ? .8 : .43);
      c.moveTo(p[0] - dy / length * tick, p[1] + dx / length * tick);
      c.lineTo(p[0] + dy / length * tick, p[1] - dx / length * tick);
    }
    c.stroke();
  }

  function floorPoint(g, a, scale = 1) {
    return [g.cx + g.rx * Math.cos(a) * scale, g.cy + g.ry * Math.sin(a) * scale];
  }

  function floor(c, base, g, w, t, angle) {
    c.save();
    c.translate(g.cx, g.cy); c.scale(g.rx, g.ry);
    const pool = c.createRadialGradient(0, 0, .1, 0, 0, 1);
    pool.addColorStop(0, "rgba(54,39,66,.3)");
    pool.addColorStop(.8, "rgba(54,39,66,.2)");
    pool.addColorStop(1, "rgba(54,39,66,0)");
    c.globalAlpha = base; c.fillStyle = pool;
    c.beginPath(); c.arc(0, 0, 1, 0, TAU); c.fill();
    c.restore();

    c.beginPath(); c.ellipse(g.cx, g.cy, g.rx, g.ry, 0, 0, TAU);
    c.globalAlpha = base * .65; c.strokeStyle = INK; c.lineWidth = w * 1.9; c.stroke();
    c.globalAlpha = base * .85;
    c.strokeStyle = metal(c, g.cx - g.rx, g.cy - g.ry, g.cx + g.rx, g.cy + g.ry);
    c.lineWidth = w * .85; c.stroke();

    c.globalAlpha = base * .5; c.strokeStyle = CHALK; c.lineWidth = Math.max(.65, w * .3);
    c.beginPath(); c.ellipse(g.cx, g.cy, g.rx * .85, g.ry * .85, 0, 0, TAU); c.stroke();

    // Compass construction arcs and witness lines replace the baseline's runes.
    c.beginPath();
    c.moveTo(g.cx - g.rx * .88, g.cy); c.lineTo(g.cx + g.rx * .88, g.cy);
    c.moveTo(g.cx, g.cy - g.ry * .8); c.lineTo(g.cx, g.cy + g.ry * .8);
    for (const side of [-1, 1]) {
      for (let i = 0; i <= 36; i++) {
        const a = -1.32 + i / 36 * 2.64;
        const x = g.cx + side * g.rx * (-.34 + .63 * Math.cos(a));
        const y = g.cy + g.ry * .63 * Math.sin(a);
        if (i === 0) c.moveTo(x, y); else c.lineTo(x, y);
      }
    }
    c.globalAlpha = base * .36; c.stroke();

    c.beginPath();
    const dimensionY = g.cy + g.ry * .49;
    c.moveTo(g.cx - g.rx * .64, dimensionY); c.lineTo(g.cx + g.rx * .64, dimensionY);
    for (const side of [-1, 1]) {
      const x = g.cx + side * g.rx * .64;
      c.moveTo(x, dimensionY - w); c.lineTo(x, dimensionY + w);
      c.moveTo(x - side * w * 2, dimensionY - w * .7);
      c.lineTo(x, dimensionY); c.lineTo(x - side * w * 2, dimensionY + w * .7);
    }
    c.globalAlpha = base * .48; c.strokeStyle = IVORY; c.stroke();

    c.beginPath();
    for (let i = 0; i < 16; i++) {
      const a = i / 16 * TAU, outer = floorPoint(g, a, .99);
      const inner = floorPoint(g, a, i % 4 === 0 ? .87 : .93);
      c.moveTo(inner[0], inner[1]); c.lineTo(outer[0], outer[1]);
    }
    c.globalAlpha = base * .65; c.strokeStyle = INK; c.lineWidth = Math.max(.7, w * .32); c.stroke();

    const phase = ((t / 6) % 1 + 1) % 1;
    c.beginPath(); c.ellipse(g.cx, g.cy, g.rx * (.3 + phase * .49), g.ry * (.3 + phase * .49), 0, 0, TAU);
    c.globalAlpha = base * Math.sin(phase * Math.PI) * .28;
    c.strokeStyle = LILAC; c.lineWidth = Math.max(.65, w * .3); c.stroke();

    const mark = floorPoint(g, angle, .82);
    c.beginPath(); c.ellipse(g.cx, g.cy, g.rx * .82, g.ry * .82, 0, angle - .15, angle + .15);
    c.globalAlpha = base * .65; c.strokeStyle = LILAC; c.lineWidth = w * .6; c.stroke();
    polygon(c, [[mark[0], mark[1] - w], [mark[0] + w * 1.5, mark[1]],
      [mark[0], mark[1] + w], [mark[0] - w * 1.5, mark[1]]]);
    c.globalAlpha = base * .8; c.fillStyle = IVORY; c.fill();
  }

  function supports(c, base, r, meridian, g, front, w, height, t) {
    for (const side of [-1, 1]) {
      const a = side === 1 ? 0 : Math.PI, pivot = point(r, a), inner = point(meridian, a);
      const foot = [g.cx + side * g.rx * .88, g.cy, -.12];
      if (!front) {
        const rail = [
          [pivot[0], pivot[1], -.12],
          [pivot[0] + side * w * 2.1, pivot[1] + height * .045, -.12],
          [foot[0], foot[1] - w * 2, -.12]
        ];
        brassLine(c, base, rail, false, w * 1.15, .84, COPPER);
        polygon(c, [[foot[0] - w * 2.4, foot[1] - w * .4], [foot[0], foot[1] - w * 2.1],
          [foot[0] + w * 2.4, foot[1] - w * .4], [foot[0] + w * 1.8, foot[1] + w],
          [foot[0] - w * 1.8, foot[1] + w]]);
        c.globalAlpha = base * .94; c.fillStyle = "#bda775"; c.fill();
        c.strokeStyle = INK; c.lineWidth = Math.max(.7, w * .4); c.stroke();
        c.beginPath(); c.moveTo(foot[0] - w, foot[1] - w * .4); c.lineTo(foot[0] + w, foot[1] - w * .4);
        c.strokeStyle = IVORY; c.globalAlpha = base * .55; c.stroke();

        if (side === 1) {
          const x = pivot[0] + w * 1.7, y = pivot[1] + height * .14;
          polygon(c, [[x - w * 1.2, y - w * 3], [x + w * .8, y - w * 3],
            [x + w * 1.4, y - w * 2], [x + w * 1.1, y + w * 2.5],
            [x - w, y + w * 2.5], [x - w * 1.4, y - w * 2]]);
          c.globalAlpha = base * .95; c.fillStyle = NAVY; c.fill();
          c.strokeStyle = COPPER; c.lineWidth = w * .6; c.stroke();
          c.beginPath(); c.ellipse(x, y - w * .2, w * .52, w * 1.55, 0, 0, TAU);
          c.globalAlpha = base * (.72 + .08 * Math.sin(t * .6));
          c.fillStyle = "#7aabd1"; c.fill();
          c.beginPath(); c.moveTo(x - w * 1.1, y - w * 2.6); c.lineTo(x + w * .9, y - w * 2.6);
          c.moveTo(x - w, y + w * 2.1); c.lineTo(x + w * 1.1, y + w * 2.1);
          c.globalAlpha = base * .9; c.strokeStyle = IVORY; c.lineWidth = w * .55; c.stroke();
        }
      }

      brassLine(c, base, [inner, pivot], front, w * 1.3, .9, COPPER);
      if ((pivot[2] >= 0) !== front) continue;
      c.beginPath(); c.arc(pivot[0], pivot[1], w * 2.1, 0, TAU);
      c.globalAlpha = base; c.fillStyle = INK; c.fill();
      c.beginPath(); c.arc(pivot[0], pivot[1] - w * .15, w * 1.55, 0, TAU);
      c.fillStyle = "#c3a472"; c.fill();
      polygon(c, [[pivot[0] - w * .85, pivot[1]], [pivot[0], pivot[1] - w * .85],
        [pivot[0] + w * .85, pivot[1]], [pivot[0], pivot[1] + w * .85]]);
      c.fillStyle = IVORY; c.fill();
      c.beginPath(); c.moveTo(pivot[0] - w * .45, pivot[1]); c.lineTo(pivot[0] + w * .45, pivot[1]);
      c.strokeStyle = INK; c.lineWidth = Math.max(.6, w * .3); c.stroke();
    }
  }

  function caliper(c, base, r, angle, front, w, ghost) {
    const p = point(r, angle);
    if ((p[2] >= 0) !== front) return;
    const q = point(r, angle + .005);
    c.save();
    c.translate(p[0], p[1]); c.rotate(Math.atan2(q[1] - p[1], q[0] - p[0]));
    const jaws = [
      [[-2.7, 1.3], [-2.7, -2.1], [2.4, -2.1], [3, -1.2], [-1.6, -1.2], [-1.6, 1.3]],
      [[1.6, -1.3], [2.7, -1.3], [2.7, 2.1], [-2.4, 2.1], [-3, 1.2], [1.6, 1.2]]
    ];
    for (let i = 0; i < jaws.length; i++) {
      polygon(c, jaws[i].map(([x, y]) => [x * w, y * w]));
      c.globalAlpha = base * (ghost || 1);
      if (!ghost) {
        c.fillStyle = i === 0 ? IVORY : "#b59a69"; c.fill();
      }
      c.strokeStyle = ghost ? LILAC : INK;
      c.lineWidth = Math.max(.65, w * .35); c.stroke();
    }
    if (!ghost) {
      c.beginPath(); c.moveTo(w * .3, -w * 2.1); c.lineTo(w * .3, w * 2.1);
      c.globalAlpha = base * .8; c.strokeStyle = INK; c.lineWidth = Math.max(.6, w * .28); c.stroke();
      c.beginPath(); c.arc(-w * 2.1, -w * 1.5, w * .43, 0, TAU);
      c.fillStyle = COPPER; c.globalAlpha = base; c.fill();
    }
    c.restore();
  }

  function echoes(c, base, r, angle, front, w, t) {
    const phase = ((t / 5.5) % 1 + 1) % 1;
    const alpha = Math.sin(phase * Math.PI) * (front ? .32 : .22);
    paths(c, split(orbit(r, 1 + phase * .075), front));
    c.globalAlpha = base * alpha * .23;
    c.strokeStyle = "#67527d"; c.lineWidth = w * 2.8; c.stroke();
    c.globalAlpha = base * alpha;
    c.strokeStyle = LILAC; c.lineWidth = Math.max(.7, w * .4); c.stroke();

    paths(c, split(orbit(r, 1.025, angle - .6, angle - .1), front));
    c.globalAlpha = base * .3; c.lineWidth = Math.max(.6, w * .3); c.stroke();
    caliper(c, base, r, angle - .32, front, w, .12);
    caliper(c, base, r, angle - .16, front, w, .25);
  }

  function plumb(c, base, r, g, angle, front, w) {
    const strength = clamp((Math.abs(Math.cos(angle)) - .65) / .25, 0, 1);
    if (strength === 0) return;
    const p = point(r, angle), q = floorPoint(g, angle, .82);
    paths(c, split([p, [q[0], q[1], p[2]]], front));
    c.globalAlpha = base * strength * .3;
    c.strokeStyle = CHALK; c.lineWidth = Math.max(.65, w * .3);
    c.setLineDash([w, w * 2.2]); c.stroke(); c.setLineDash([]);
  }

  function draw(context, bounds, time, opacity, front) {
    if (!(opacity > 0) || !(context.globalAlpha > 0)) return;
    const [left, top, width, height] = bounds;
    if (!(width > 0 && height > 0)) return;
    const c = context, t = Number.isFinite(time) ? time : 0;
    const cx = left + width * .5, w = clamp(height * .0075, 1.6, 2.9);
    const lean = .085 * Math.sin(t * .17), ry = clamp(height * .105, 24, 40);
    const equator = {
      cx, cy: top + height * .545,
      u: [width * .7 * Math.cos(lean), width * .7 * Math.sin(lean), 0],
      v: [-ry * Math.sin(lean), ry * Math.cos(lean), 1]
    };
    const yaw = .24 + .065 * Math.sin(t * .13);
    const meridian = {
      cx, cy: top + height * .495,
      u: [width * .565 * Math.cos(yaw), 0, Math.sin(yaw)],
      v: [0, -height * .435, -.75]
    };
    const groundHeight = clamp(height * .036, 8, 13);
    // Even the ground rim's underpaint ends above the feet, not in the health bar.
    const g = { cx, cy: top + height - groundHeight - 5, rx: width * .76, ry: groundHeight };
    const angle = -.44 + t * .115;

    c.save();
    try {
      const base = c.globalAlpha * Math.min(opacity, 1);
      c.globalAlpha = base;
      c.globalCompositeOperation = "source-over";
      c.lineCap = "round"; c.lineJoin = "round"; c.setLineDash([]);
      c.lineDashOffset = 0;
      if (!front) floor(c, base, g, w, t, angle);
      supports(c, base, equator, meridian, g, front, w, height, t);
      echoes(c, base, equator, angle, front, w, t);
      band(c, base, meridian, front, w * 1.15, front ? .87 : .58, 24);
      band(c, base, equator, front, w * 1.75, front ? 1 : .65, 32);
      plumb(c, base, equator, g, angle, front, w);
      caliper(c, base, equator, angle, front, w, 0);
    } finally {
      c.restore();
    }
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies["sol-architect"] = {
    name: "Surveyor's Armillary",
    description: "Brass side supports seat the torso bands on a chalk floor plan. An ivory caliper leaves lilac copies of its previous readings.",
    fit: "Ivory fittings, copper pipework and compass diagrams match the Architect's workshop.",
    tradeoff: "Two side rails make the apparatus more grounded and less free-floating.",
    implementation: "Depth-split bands and yokes, a sliding caliper, and a matching ground projection.",
    draw
  };
})();
