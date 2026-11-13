(function () {
  "use strict";

  const TAU = Math.PI * 2;
  const PLUM = "#2b2134", WASH = "#5d4a7c", LILAC = "#c3b2de", IVORY = "#f1e3bd";
  const BRONZE = [[0, "#3f5d55"], [.3, "#7f9a7f"], [.52, "#d0aa5f"], [.78, "#8a6a3a"], [1, "#46584f"]];
  // Hand-cut glass shard outlines; each orbiting shard uses a different silhouette.
  const SHARDS = [
    [[0, -1.45], [.62, -.25], [.3, 1.05], [-.55, .4]],
    [[-.2, -1.3], [.7, -.55], [.45, .95], [-.6, .7], [-.5, -.3]],
    [[.1, -1.5], [.55, .2], [-.15, 1.1], [-.5, -.15]],
    [[-.35, -1.2], [.5, -1.0], [.6, .55], [-.1, 1.15], [-.6, .1]],
    [[0, -1.35], [.68, .1], [.05, 1.2], [-.62, -.05]]
  ];

  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const frac = v => v - Math.floor(v);

  // A ring is a 3D circle projected orthographically: u and v carry [screenX, screenY, depth].
  function ring(cx, cy, u, v) { return { cx, cy, u, v }; }
  function point(r, a, s = 1) {
    const c = Math.cos(a), n = Math.sin(a);
    return [r.cx + s * (r.u[0] * c + r.v[0] * n), r.cy + s * (r.u[1] * c + r.v[1] * n), r.u[2] * c + r.v[2] * n];
  }
  function tangent(r, a) {
    const c = Math.cos(a), n = Math.sin(a);
    const x = -r.u[0] * n + r.v[0] * c, y = -r.u[1] * n + r.v[1] * c, l = Math.hypot(x, y) || 1;
    return [x / l, y / l];
  }
  // Contiguous runs of the ring on one side of the body; each run overlaps the crossover by one sample.
  function runs(r, front, s = 1, n = 72) {
    const out = [];
    let cur = null, prev = null, startsAtZero = false;
    for (let i = 0; i <= n; i++) {
      const p = point(r, i / n * TAU, s), match = (p[2] >= 0) === front;
      if (match) {
        if (!cur) { cur = prev ? [prev] : []; out.push(cur); if (i === 0) startsAtZero = true; }
        cur.push(p);
      } else if (cur) { cur.push(p); cur = null; }
      prev = p;
    }
    if (out.length > 1 && startsAtZero && cur) {
      const first = out.shift();
      out[out.length - 1] = cur.concat(first.slice(1));
    }
    return out;
  }
  function path(c, pts, dy = 0) {
    c.beginPath();
    pts.forEach((p, i) => i ? c.lineTo(p[0], p[1] + dy) : c.moveTo(p[0], p[1] + dy));
  }
  function bronze(c, x0, y0, x1, y1) {
    const g = c.createLinearGradient(x0, y0, x1, y1);
    BRONZE.forEach(([o, col]) => g.addColorStop(o, col));
    return g;
  }

  // A patinated bronze ribbon: plum underpaint, metal body, ivory catch-light, engraved scale ticks.
  function band(c, base, r, front, w, strength) {
    const ex = Math.abs(r.u[0]) + Math.abs(r.v[0]), ey = Math.abs(r.u[1]) + Math.abs(r.v[1]);
    const metal = bronze(c, r.cx - ex, r.cy - ey, r.cx + ex, r.cy + ey);
    for (const pts of runs(r, front)) {
      path(c, pts); c.globalAlpha = base * strength * .5; c.strokeStyle = PLUM; c.lineWidth = w * 1.9; c.stroke();
      c.globalAlpha = base * strength; c.strokeStyle = metal; c.lineWidth = w; c.stroke();
      path(c, pts, -w * .28); c.globalAlpha = base * strength * (front ? .6 : .3);
      c.strokeStyle = IVORY; c.lineWidth = Math.max(.6, w * .26); c.stroke();
    }
    c.globalAlpha = base * strength * .7; c.strokeStyle = PLUM; c.lineWidth = Math.max(.7, w * .2);
    c.beginPath();
    for (let k = 0; k < 28; k++) {
      const a = k / 28 * TAU, p = point(r, a);
      if ((p[2] >= 0) !== front) continue;
      const [tx, ty] = tangent(r, a), len = w * (k % 7 === 0 ? .75 : .45);
      c.moveTo(p[0] + ty * len, p[1] - tx * len); c.lineTo(p[0] - ty * len, p[1] + tx * len);
    }
    c.stroke();
  }

  // Lilac afterimages that peel off a band and drift outward, like a struck bell's echo.
  function bandEchoes(c, base, r, front, w, t, period, growth, strength) {
    for (let k = 0; k < 2; k++) {
      const ph = frac(t / period + k / 2), s = 1 + growth * ph;
      const a = strength * (1 - ph) * Math.min(1, ph * 5);
      if (a <= 0) continue;
      for (const pts of runs(r, front, s)) {
        path(c, pts); c.globalAlpha = base * a * .22; c.strokeStyle = WASH; c.lineWidth = w * 2.4; c.stroke();
        c.globalAlpha = base * a * .8; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .32); c.stroke();
      }
    }
  }

  function shard(c, base, x, y, size, rot, shape, alpha, ghost, w) {
    c.save();
    c.translate(x, y); c.rotate(rot);
    c.beginPath();
    shape.forEach(([px, py], i) => i ? c.lineTo(px * size, py * size) : c.moveTo(px * size, py * size));
    c.closePath();
    c.lineJoin = "round";
    if (ghost) {
      c.globalAlpha = base * alpha; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .4); c.stroke();
    } else {
      const glass = c.createLinearGradient(-size, -size, size, size);
      glass.addColorStop(0, "#e6dcf0"); glass.addColorStop(.45, "#a796c4"); glass.addColorStop(1, "#4f4270");
      c.globalAlpha = base * alpha; c.fillStyle = glass; c.fill();
      c.strokeStyle = PLUM; c.lineWidth = Math.max(.8, w * .5); c.stroke();
      c.beginPath(); c.moveTo(shape[0][0] * size, shape[0][1] * size); c.lineTo(shape[2][0] * size * .4, shape[2][1] * size * .4);
      c.globalAlpha = base * alpha * .8; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .3); c.stroke();
    }
    c.restore();
  }

  function shards(c, base, r, front, t, size, w) {
    for (let i = 0; i < 5; i++) {
      const a = t * .4 + i * TAU / 5, p = point(r, a);
      if ((p[2] >= 0) !== front) continue;
      const depth = clamp(p[2], -1, 1), s = size * (.85 + .2 * depth), alpha = front ? 1 : .55;
      for (let e = 2; e >= 1; e--) {
        const q = point(r, a - e * .13);
        if ((q[2] >= 0) === front) shard(c, base, q[0], q[1], s * (1 + e * .08), (a - e * .13) * .7 + i, SHARDS[i], alpha * (.36 - e * .12), true, w);
      }
      shard(c, base, p[0], p[1], s, a * .7 + i, SHARDS[i], alpha, false, w);
    }
  }

  // Ground: a plum pool with ripples spreading from the feet, a broken bronze rim, and engraved runes.
  function ground(c, base, cx, fy, rx, ry, w, t) {
    c.save();
    c.translate(cx, fy); c.scale(rx * 1.06, ry * 1.2);
    const pool = c.createRadialGradient(0, 0, 0, 0, 0, 1);
    pool.addColorStop(0, "rgba(43,33,52,.55)"); pool.addColorStop(.7, "rgba(60,44,78,.35)"); pool.addColorStop(1, "rgba(60,44,78,0)");
    c.globalAlpha = base; c.fillStyle = pool; c.beginPath(); c.arc(0, 0, 1, 0, TAU); c.fill();
    c.restore();

    for (let i = 0; i < 3; i++) {
      const ph = frac(t / 4.2 + i / 3), s = .3 + .72 * ph, a = (1 - ph) * Math.min(1, ph * 4);
      c.beginPath(); c.ellipse(cx, fy, rx * s, ry * s, 0, 0, TAU);
      c.globalAlpha = base * a * .25; c.strokeStyle = WASH; c.lineWidth = w * 2.2; c.stroke();
      c.globalAlpha = base * a * .75; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .35); c.stroke();
    }

    c.globalAlpha = base * .7; c.strokeStyle = "#c9a45c"; c.lineWidth = Math.max(.6, w * .3);
    c.setLineDash([w * 1.6, w * 2.8]); c.lineDashOffset = -t * w * 1.5;
    c.beginPath(); c.ellipse(cx, fy, rx * .7, ry * .7, 0, 0, TAU); c.stroke();
    c.setLineDash([]);

    const metal = bronze(c, cx - rx, fy - ry, cx + rx, fy + ry), spin = t * .07;
    c.lineCap = "round";
    for (let i = 0; i < 6; i++) {
      const a0 = spin + i * TAU / 6 + .09, a1 = a0 + TAU / 6 - .18;
      c.beginPath(); c.ellipse(cx, fy, rx, ry, 0, a0, a1);
      c.globalAlpha = base * .5; c.strokeStyle = PLUM; c.lineWidth = w * 2.2; c.stroke();
      c.globalAlpha = base; c.strokeStyle = metal; c.lineWidth = w * 1.15; c.stroke();
      c.beginPath(); c.ellipse(cx, fy - w * .3, rx, ry, 0, a0 + .1, a1 - .1);
      c.globalAlpha = base * .45; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .25); c.stroke();
    }

    c.globalAlpha = base * .85; c.strokeStyle = "#d8b56a"; c.lineWidth = Math.max(.6, w * .38);
    c.beginPath();
    for (let i = 0; i < 12; i++) {
      const a = -spin * 1.6 + i * TAU / 12, x = cx + Math.cos(a) * rx * .85, y = fy + Math.sin(a) * ry * .85;
      const h = ry * (i % 3 === 0 ? .32 : .2), arm = w * (i % 2 ? .9 : 1.4);
      c.moveTo(x, y + h * .5); c.lineTo(x, y - h * .5);
      c.moveTo(x - arm, y - h * .5); c.lineTo(x, y - h * .1); c.lineTo(x + arm, y - h * .5);
    }
    c.stroke();

    // Four splinters of echo glass planted in the rim, drawn behind the feet.
    for (let i = 0; i < 4; i++) {
      const a = spin + i * TAU / 4 + TAU / 12, x = cx + Math.cos(a) * rx, y = fy + Math.sin(a) * ry;
      shard(c, base, x, y - w * 1.6, w * 1.6, (i % 2 ? .25 : -.2), SHARDS[i], .9, false, w * .8);
    }
  }

  function draw(context, bounds, time, opacity, front) {
    if (!(opacity > 0)) return;
    const c = context, [left, top, width, height] = bounds, t = time || 0;
    const cx = left + width * .5, w = clamp(height * .0075, 1.4, 3);
    c.save();
    c.globalAlpha *= Math.min(1, opacity);
    const base = c.globalAlpha;
    c.lineCap = "round"; c.lineJoin = "round"; c.globalCompositeOperation = "source-over";

    const tilt = .3 * Math.sin(t * .35), rx = width * .74, ry = height * .2;
    const equator = ring(cx, top + height * .56,
      [rx * Math.cos(tilt), rx * Math.sin(tilt), 0], [-ry * Math.sin(tilt), ry * Math.cos(tilt), 1]);
    const phi = .5 * Math.sin(t * .27 + 1), R = width * .56, H = height * .49;
    // Leaning back keeps the meridian's top arc behind the head and its foot arc in front.
    const meridian = ring(cx, top + height * .5, [R * Math.cos(phi), 0, Math.sin(phi)], [0, -H, -.35]);

    if (!front) ground(c, base, cx, top + height - 2, width * .78, clamp(height * .05, 8, 15), w, t);
    // The meridian's echo grows less so its foot arc stays well clear of the health bar.
    bandEchoes(c, base, meridian, front, w * .8, t + 1.7, 4.6, .06, front ? .45 : .3);
    band(c, base, meridian, front, w * .85, front ? .8 : .45);
    bandEchoes(c, base, equator, front, w * 1.6, t, 3.4, .13, front ? .6 : .35);
    band(c, base, equator, front, w * 1.6, front ? .95 : .55);
    shards(c, base, equator, front, t, clamp(height * .03, 5, 11), w);
    c.restore();
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies.opus = {
    name: "Verdigris Armillary",
    description: "Patinated bronze armillary bands, one equatorial and one leaning meridian, wrap the body while lilac afterimages peel off them like a struck bell's echo. Orbiting shards of echo glass trail ghost copies, and a plum pool with spreading ripples and a broken, rune-engraved bronze rim anchors the feet.",
    draw
  };
})();
