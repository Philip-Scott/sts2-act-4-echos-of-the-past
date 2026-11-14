(function () {
  "use strict";

  const TAU = Math.PI * 2;
  const PLUM = "#2b2134", WASH = "#5d4a7c", LILAC = "#c3b2de", IVORY = "#f1e3bd", CHALK = "#e9e1cc", GOLD = "#d8b56a";
  const BRONZE = [[0, "#3f5d55"], [.3, "#7f9a7f"], [.52, "#d0aa5f"], [.78, "#8a6a3a"], [1, "#46584f"]];
  const COPPER = [[0, "#3d5a50"], [.22, "#7a4b2e"], [.5, "#d4935a"], [.76, "#8c5233"], [1, "#4a5f52"]];
  const STAFF = [143, 184, 255], ECHO = [195, 178, 222], SPARK = [236, 244, 255];

  // Every motion is a fraction of one shared beat: the dial steps, the read head sparks,
  // the pulse climbs the meridian, then the equator register steps and rings out its echo.
  const BEAT = 4.4, NOTCH = TAU / 12;
  const DIAL = [0, .2], IGNITE = .22, CLIMB = [.24, .58], CARRY = [.58, .8];

  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const smooth = x => x * x * (3 - 2 * x);
  const bump = (x, a, b) => x > a && x < b ? Math.sin(Math.PI * (x - a) / (b - a)) : 0;
  const rgb = (a, b, k, alpha = 1) =>
    `rgba(${Math.round(a[0] + (b[0] - a[0]) * k)},${Math.round(a[1] + (b[1] - a[1]) * k)},${Math.round(a[2] + (b[2] - a[2]) * k)},${alpha})`;

  // A pawl dropping into a tooth: slight wind-up, eased travel, slight settle.
  function ratchet(x) {
    const k = 1.1, y = 2 * x;
    return x < .5 ? y * y * ((k + 1) * y - k) / 2 : ((y - 2) * (y - 2) * ((k + 1) * (y - 2) + k) + 2) / 2;
  }
  function stepped(t, win) {
    const n = Math.floor(t / BEAT), ph = t / BEAT - n;
    return n % 12 + ratchet(clamp((ph - win[0]) / (win[1] - win[0]), 0, 1));
  }
  // Ages of the latest two occurrences of a once-per-beat event.
  function ages(t, at) {
    const n = Math.floor(t / BEAT - at), age = t - (n + at) * BEAT;
    return [age, age + BEAT];
  }

  // A ring is a 3D circle projected orthographically: u and v carry [screenX, screenY, depth].
  function ring(cx, cy, u, v) { return { cx, cy, u, v }; }
  function scaled(r, su, sv) {
    return ring(r.cx, r.cy, [r.u[0] * su, r.u[1] * su, r.u[2]], [r.v[0] * sv, r.v[1] * sv, r.v[2]]);
  }
  function point(r, a) {
    const c = Math.cos(a), n = Math.sin(a);
    return [r.cx + r.u[0] * c + r.v[0] * n, r.cy + r.u[1] * c + r.v[1] * n, r.u[2] * c + r.v[2] * n];
  }
  function tangent(r, a) {
    const c = Math.cos(a), n = Math.sin(a);
    const x = -r.u[0] * n + r.v[0] * c, y = -r.u[1] * n + r.v[1] * c, l = Math.hypot(x, y) || 1;
    return [x / l, y / l];
  }
  // Contiguous runs of the ring on one side of the body; each run overlaps the crossover by one sample.
  function runs(r, front, n = 72) {
    const out = [];
    let cur = null, prev = null, startsAtZero = false;
    for (let i = 0; i <= n; i++) {
      const p = point(r, i / n * TAU), match = (p[2] >= 0) === front;
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
  function metal(c, stops, x0, y0, x1, y1) {
    const g = c.createLinearGradient(x0, y0, x1, y1);
    stops.forEach(([o, col]) => g.addColorStop(o, col));
    return g;
  }
  function segment(c, a, b) { c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); }

  // Meridian parameter: lam 0 is the foot in the ground cradle, lam 1 the crown; side picks left or right half.
  const side = (s, lam) => -Math.PI / 2 + s * lam * Math.PI;

  // Where a meridian half first crosses the equator's front arc, solved in screen space each frame.
  function crossing(mer, eq, s) {
    const [u0, u1] = eq.u, [v0, v1] = eq.v, det = u0 * v1 - v0 * u1;
    const local = p => {
      const dx = p[0] - eq.cx, dy = p[1] - eq.cy;
      return [(dx * v1 - v0 * dy) / det, (u0 * dy - dx * u1) / det];
    };
    let prevL = 0, prevF = 1;
    for (let i = 0; i <= 40; i++) {
      const lam = i / 40 * .8, [c, n] = local(point(mer, side(s, lam))), f = c * c + n * n - 1;
      if (i && prevF > 0 && f <= 0) {
        const hit = prevL + (lam - prevL) * prevF / (prevF - f), p = point(mer, side(s, hit)), [c2, n2] = local(p);
        return { s, lam: hit, theta: Math.atan2(n2, c2), p, a: side(s, hit) };
      }
      prevL = lam; prevF = f;
    }
    return null;
  }

  // A patinated bronze ribbon with engraved scale ticks; `turn` slides the engraving along the band.
  function band(c, base, r, front, w, strength, turn) {
    const ex = Math.abs(r.u[0]) + Math.abs(r.v[0]), ey = Math.abs(r.u[1]) + Math.abs(r.v[1]);
    const body = metal(c, BRONZE, r.cx - ex, r.cy - ey, r.cx + ex, r.cy + ey);
    for (const pts of runs(r, front)) {
      path(c, pts); c.globalAlpha = base * strength * .5; c.strokeStyle = PLUM; c.lineWidth = w * 1.9; c.stroke();
      c.globalAlpha = base * strength; c.strokeStyle = body; c.lineWidth = w; c.stroke();
      path(c, pts, -w * .28); c.globalAlpha = base * strength * (front ? .6 : .3);
      c.strokeStyle = IVORY; c.lineWidth = Math.max(.6, w * .26); c.stroke();
    }
    c.globalAlpha = base * strength * .7; c.strokeStyle = PLUM; c.lineWidth = Math.max(.7, w * .2);
    c.beginPath();
    for (let k = 0; k < 24; k++) {
      const a = k / 24 * TAU + turn, p = point(r, a);
      if ((p[2] >= 0) !== front) continue;
      const [tx, ty] = tangent(r, a), len = w * (k % 6 === 0 ? .8 : .45);
      c.moveTo(p[0] + ty * len, p[1] - tx * len); c.lineTo(p[0] - ty * len, p[1] + tx * len);
    }
    c.stroke();
  }

  // Lilac afterimages released by a beat event; `su`/`sv` choose which axes the echo spreads along.
  function echoes(c, base, r, front, w, eventAges, life, su, sv, strength) {
    for (const age of eventAges) {
      const ph = age / life;
      if (ph >= 1) continue;
      const a = strength * (1 - ph) * Math.min(1, ph * 6), k = smooth(ph);
      for (const pts of runs(scaled(r, 1 + su * k, 1 + sv * k), front)) {
        path(c, pts); c.globalAlpha = base * a * .22; c.strokeStyle = WASH; c.lineWidth = w * 2.4; c.stroke();
        c.globalAlpha = base * a * .8; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .32); c.stroke();
      }
    }
  }

  function glyph(c, kind, x, y, h) {
    const k = h * .5, m = h * .32;
    if (kind === 0) {
      c.moveTo(x, y + k); c.lineTo(x, y - k);
      c.moveTo(x - m, y - k); c.lineTo(x, y - k * .2); c.lineTo(x + m, y - k);
    } else if (kind === 1) {
      c.moveTo(x + m * .8, y - k * .35); c.arc(x, y - k * .35, m * .8, 0, TAU);
      c.moveTo(x, y + k * .45); c.lineTo(x, y + k);
    } else if (kind === 2) { // drafting compass
      c.moveTo(x - m, y + k); c.lineTo(x, y - k); c.lineTo(x + m, y + k);
      c.moveTo(x - m * .55, y + k * .1); c.lineTo(x + m * .55, y + k * .1);
    } else { // set square
      c.moveTo(x - m, y + k); c.lineTo(x + m, y + k); c.lineTo(x - m, y - k); c.closePath();
    }
  }

  // Ground dial: copper pipe stator with flanged joints, a chalk-and-gold dial rotor that ratchets
  // one glyph per beat to the front read slot, and ripples launched by each read.
  function ground(c, base, g, w, t, dialA, read, ign) {
    const { cx, fy, rx, ry, footY } = g;
    c.save();
    c.translate(cx, fy); c.scale(rx * 1.06, ry * 1.2);
    const pool = c.createRadialGradient(0, 0, 0, 0, 0, 1);
    pool.addColorStop(0, "rgba(43,33,52,.55)"); pool.addColorStop(.7, "rgba(60,44,78,.35)"); pool.addColorStop(1, "rgba(60,44,78,0)");
    c.globalAlpha = base; c.fillStyle = pool; c.beginPath(); c.arc(0, 0, 1, 0, TAU); c.fill();
    c.restore();

    for (const age of ages(t, IGNITE)) {
      const ph = age / (BEAT * 1.8);
      if (ph >= 1) continue;
      const s = .3 + .74 * ph, a = (1 - ph) * Math.min(1, ph * 5);
      c.beginPath(); c.ellipse(cx, fy, rx * s, ry * s, 0, 0, TAU);
      c.globalAlpha = base * a * .25; c.strokeStyle = WASH; c.lineWidth = w * 2.2; c.stroke();
      c.globalAlpha = base * a * .75; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .35); c.stroke();
    }

    // Chalk construction: a dimension line across the dial and a dashed compass circle turning with the rotor.
    c.strokeStyle = CHALK; c.lineWidth = Math.max(.5, w * .28); c.globalAlpha = base * .24;
    c.beginPath(); c.moveTo(cx - rx * .94, fy); c.lineTo(cx + rx * .94, fy);
    for (const sx of [-1, 1]) { c.moveTo(cx + sx * rx * .94, fy - ry * .24); c.lineTo(cx + sx * rx * .94, fy + ry * .24); }
    c.stroke();
    c.setLineDash([w * 1.2, w * 2.2]); c.lineDashOffset = -dialA * rx * .58;
    c.globalAlpha = base * .34; c.beginPath(); c.ellipse(cx, fy, rx * .58, ry * .58, 0, 0, TAU); c.stroke();
    c.setLineDash([]); c.lineDashOffset = 0;

    c.globalAlpha = base * .6; c.strokeStyle = GOLD; c.lineWidth = Math.max(.5, w * .3);
    c.beginPath();
    for (let i = 0; i < 36; i++) {
      const a = dialA + i * TAU / 36, co = Math.cos(a), si = Math.sin(a), r0 = i % 3 === 0 ? .64 : .68;
      c.moveTo(cx + co * rx * r0, fy + si * ry * r0); c.lineTo(cx + co * rx * .73, fy + si * ry * .73);
    }
    c.stroke();

    // Read slot: the glyph at the front glows and its light runs up into the meridian cradle.
    if (read > 0) {
      const y0 = fy + ry * .86, y1 = footY + w * 1.4;
      segment(c, [cx, y0], [cx, y1]);
      c.globalAlpha = base * read * .35; c.strokeStyle = WASH; c.lineWidth = w * 2.6; c.stroke();
      c.globalAlpha = base * read * .9; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .5); c.stroke();
    }
    for (let i = 0; i < 12; i++) {
      const a = Math.PI / 2 + dialA + i * NOTCH;
      const off = Math.abs(((a - Math.PI / 2) % TAU + TAU + Math.PI) % TAU - Math.PI);
      const hot = read * Math.max(0, 1 - off / (NOTCH * .5)), x = cx + Math.cos(a) * rx * .86, y = fy + Math.sin(a) * ry * .86;
      const h = Math.max(3, ry * (.36 + .08 * Math.sin(a)));
      c.beginPath(); glyph(c, i % 4, x, y, h);
      if (hot > 0) {
        c.globalAlpha = base * hot * .45; c.strokeStyle = WASH; c.lineWidth = w * 2; c.stroke();
      }
      c.globalAlpha = base * .85; c.strokeStyle = hot > .3 ? IVORY : GOLD; c.lineWidth = Math.max(.6, w * .38); c.stroke();
    }

    const pipe = metal(c, COPPER, cx - rx, fy - ry, cx + rx, fy + ry);
    for (let k = 0; k < 6; k++) {
      const a0 = Math.PI / 2 + k * TAU / 6 + .15, a1 = Math.PI / 2 + (k + 1) * TAU / 6 - .15;
      c.beginPath(); c.ellipse(cx, fy, rx, ry, 0, a0, a1);
      c.globalAlpha = base * .5; c.strokeStyle = PLUM; c.lineWidth = w * 2.3; c.stroke();
      c.globalAlpha = base; c.strokeStyle = pipe; c.lineWidth = w * 1.25; c.stroke();
      c.beginPath(); c.ellipse(cx, fy - w * .3, rx, ry, 0, a0 + .08, a1 - .08);
      c.globalAlpha = base * .45; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .25); c.stroke();
      for (const a of [a0, a1]) {
        const x = cx + Math.cos(a) * rx, y = fy + Math.sin(a) * ry;
        let nx = Math.cos(a) / rx, ny = Math.sin(a) / ry;
        const l = Math.hypot(nx, ny) || 1, len = w * 1.25;
        nx /= l; ny /= l;
        segment(c, [x - nx * len, y - ny * len], [x + nx * len, y + ny * len]);
        c.globalAlpha = base; c.strokeStyle = "#6a3f28"; c.lineWidth = w * 1.1; c.stroke();
        c.globalAlpha = base * .8; c.strokeStyle = "#e3ab6c"; c.lineWidth = Math.max(.5, w * .35); c.stroke();
      }
    }

    // Base plate the meridian cradle stands on.
    c.beginPath(); c.ellipse(cx, footY + w * 1.7, w * 3.8, w * 1.3, 0, 0, TAU);
    c.globalAlpha = base * .7; c.fillStyle = PLUM; c.fill();
    c.globalAlpha = base * (.6 + .4 * ign); c.strokeStyle = "#b98a4c"; c.lineWidth = Math.max(.6, w * .45); c.stroke();
  }

  // The meridian rests in a bronze cradle at the front of the dial; a staff-blue pilot jewel sparks each read.
  function cradle(c, base, x, y, w, ign) {
    c.beginPath();
    c.moveTo(x - w * 2.8, y + w * 1.8); c.lineTo(x + w * 2.8, y + w * 1.8);
    c.lineTo(x + w * 1.7, y + w * .15); c.lineTo(x - w * 1.7, y + w * .15); c.closePath();
    c.globalAlpha = base; c.fillStyle = metal(c, BRONZE, x - w * 3, y, x + w * 3, y + w * 2); c.fill();
    c.strokeStyle = PLUM; c.lineWidth = Math.max(.7, w * .45); c.stroke();
    for (const sx of [-1, 1]) {
      segment(c, [x + sx * w * 1.25, y - w * 1.3], [x + sx * w * 1.25, y + w * .4]);
      c.globalAlpha = base; c.strokeStyle = PLUM; c.lineWidth = w * 1.05; c.stroke();
      c.strokeStyle = "#c9a45c"; c.lineWidth = w * .6; c.stroke();
    }
    segment(c, [x - w * 2.4, y + w * .5], [x + w * 2.4, y + w * .5]);
    c.globalAlpha = base * .55; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .25); c.stroke();

    const jy = y + w * 1.05, r = w * (1.6 + 1.6 * ign);
    const glow = c.createRadialGradient(x, jy, 0, x, jy, r);
    glow.addColorStop(0, rgb(SPARK, SPARK, 0, .9)); glow.addColorStop(.35, rgb(STAFF, STAFF, 0, .65)); glow.addColorStop(1, rgb(STAFF, STAFF, 0, 0));
    c.globalAlpha = base * (.35 + .65 * ign); c.fillStyle = glow; c.beginPath(); c.arc(x, jy, r, 0, TAU); c.fill();
    c.globalAlpha = base; c.fillStyle = rgb(STAFF, SPARK, ign); c.beginPath(); c.arc(x, jy, w * .55, 0, TAU); c.fill();
  }

  function finial(c, base, x, y, w) {
    c.globalAlpha = base * .7; c.fillStyle = metal(c, BRONZE, x - w * 1.4, y - w * 1.4, x + w * 1.4, y + w * 1.4);
    c.beginPath(); c.arc(x, y, w * 1.3, 0, TAU); c.fill();
    c.strokeStyle = PLUM; c.lineWidth = Math.max(.6, w * .4); c.stroke();
    c.globalAlpha = base * .45; c.fillStyle = IVORY; c.beginPath(); c.arc(x - w * .4, y - w * .45, w * .38, 0, TAU); c.fill();
  }

  // Echo pulse climbing both meridian halves from the cradle to the equator crossings,
  // born staff-blue at the cradle and cooling to lilac as it rises.
  function pulse(c, base, mer, front, nodes, cp, w) {
    if (cp <= 0 || cp >= 1.1) return;
    const k = smooth(Math.min(1, cp)), fade = cp > 1 ? 1 - (cp - 1) / .1 : Math.min(1, cp * 6);
    const tone = smooth(clamp(cp * 1.6, 0, 1));
    for (const nd of nodes) {
      const head = nd.lam * k, tail = Math.max(0, head - .16), N = 8;
      let prev = point(mer, side(nd.s, tail));
      for (let i = 1; i <= N; i++) {
        const p = point(mer, side(nd.s, tail + (head - tail) * i / N));
        if ((p[2] + prev[2] >= 0) === front) {
          const a = fade * Math.pow(i / N, 1.4);
          segment(c, prev, p);
          c.globalAlpha = base * a * .3; c.strokeStyle = WASH; c.lineWidth = w * 2.6; c.stroke();
          c.globalAlpha = base * a; c.strokeStyle = rgb(STAFF, ECHO, tone); c.lineWidth = w * .8; c.stroke();
        }
        prev = p;
      }
      if ((prev[2] >= 0) !== front) continue;
      const r = w * 2.6, g = c.createRadialGradient(prev[0], prev[1], 0, prev[0], prev[1], r);
      g.addColorStop(0, rgb(STAFF, ECHO, tone, .7)); g.addColorStop(1, rgb(STAFF, ECHO, tone, 0));
      c.globalAlpha = base * fade; c.fillStyle = g; c.beginPath(); c.arc(prev[0], prev[1], r, 0, TAU); c.fill();
      c.fillStyle = "#f6f0ff"; c.beginPath(); c.arc(prev[0], prev[1], w * .75, 0, TAU); c.fill();
    }
  }

  // On arrival the pulse spreads into the equator band from each crossing.
  function charge(c, base, eq, front, nodes, q, w) {
    if (q <= 0 || q >= 1) return;
    const spread = .12 + .9 * smooth(q), fade = (1 - q) * Math.min(1, q * 8), N = 16;
    for (const nd of nodes) {
      for (let i = 0; i < N; i++) {
        const a0 = nd.theta - spread + 2 * spread * i / N, a1 = a0 + 2 * spread / N;
        const p0 = point(eq, a0), p1 = point(eq, a1);
        if ((p0[2] + p1[2] >= 0) !== front) continue;
        const a = fade * (1 - Math.abs((a0 + a1) / 2 - nd.theta) / spread);
        segment(c, p0, p1);
        c.globalAlpha = base * a * .28; c.strokeStyle = WASH; c.lineWidth = w * 2.8; c.stroke();
        c.globalAlpha = base * a * .9; c.strokeStyle = LILAC; c.lineWidth = w * .7; c.stroke();
      }
    }
  }

  // Bronze collars clamping the equator to the meridian where they cross.
  function collars(c, base, mer, front, nodes, w) {
    for (const nd of nodes) {
      if ((nd.p[2] >= 0) !== front) continue;
      const [tx, ty] = tangent(mer, nd.a), len = w * 1.5, alpha = front ? 1 : .55;
      segment(c, [nd.p[0] - ty * len, nd.p[1] + tx * len], [nd.p[0] + ty * len, nd.p[1] - tx * len]);
      c.globalAlpha = base * alpha; c.strokeStyle = PLUM; c.lineWidth = w * 1.6; c.stroke();
      c.strokeStyle = "#b98a4c"; c.lineWidth = w * 1.05; c.stroke();
      c.globalAlpha = base * alpha * .7; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .3); c.stroke();
    }
  }

  function capsule(c, r, half) {
    c.beginPath();
    c.moveTo(-r, -half + r); c.arc(0, -half + r, r, Math.PI, 0);
    c.lineTo(r, half - r); c.arc(0, half - r, r, 0, Math.PI); c.closePath();
  }

  // A brass-capped energy vessel; its lilac charge flares pale blue when a pulse arrives.
  function vessel(c, alpha, x, y, s, glow, flash, w, ghost) {
    const r = s * .21, half = s * .31, cap = s * .16;
    c.save();
    c.translate(x, y);
    if (ghost) {
      capsule(c, r * 1.05, half + cap * .6);
      c.globalAlpha = alpha; c.strokeStyle = LILAC; c.lineWidth = Math.max(.6, w * .4); c.stroke();
      c.restore();
      return;
    }
    const halo = c.createRadialGradient(0, 0, 0, 0, 0, s);
    halo.addColorStop(0, rgb(ECHO, SPARK, flash * .5, .45)); halo.addColorStop(1, rgb(ECHO, ECHO, 0, 0));
    c.globalAlpha = alpha * glow; c.fillStyle = halo; c.beginPath(); c.arc(0, 0, s, 0, TAU); c.fill();

    capsule(c, r, half);
    c.globalAlpha = alpha * .92; c.fillStyle = "#2a2238"; c.fill();
    c.save();
    c.clip();
    const level = half - (half * 2) * .68, fluid = c.createLinearGradient(0, level, 0, half);
    fluid.addColorStop(0, rgb(ECHO, SPARK, flash * .7, 1)); fluid.addColorStop(1, rgb([93, 74, 124], ECHO, glow * .5, 1));
    c.globalAlpha = alpha * (.45 + .55 * glow); c.fillStyle = fluid; c.fillRect(-r, level, r * 2, half - level);
    c.restore();
    capsule(c, r, half);
    c.globalAlpha = alpha; c.strokeStyle = PLUM; c.lineWidth = Math.max(.7, w * .45); c.stroke();
    segment(c, [-r * .45, -half * .45], [-r * .45, half * .35]);
    c.globalAlpha = alpha * .7; c.strokeStyle = IVORY; c.lineWidth = Math.max(.5, w * .3); c.stroke();

    const brass = metal(c, BRONZE, -r * 1.3, 0, r * 1.3, 0);
    c.globalAlpha = alpha; c.fillStyle = brass; c.strokeStyle = PLUM; c.lineWidth = Math.max(.6, w * .35);
    c.beginPath(); c.rect(-r * 1.2, -half - cap * .6, r * 2.4, cap); c.fill(); c.stroke();
    c.beginPath(); c.rect(-r * 1.05, half - cap * .4, r * 2.1, cap * .8); c.fill(); c.stroke();
    c.beginPath(); c.arc(0, -half - cap * .9, cap * .45, Math.PI, 0); c.strokeStyle = "#a98245"; c.lineWidth = Math.max(.6, w * .4); c.stroke();
    c.restore();
  }

  function vessels(c, base, eq, front, turn, size, w, glow, flash, carry) {
    const motion = Math.sin(Math.PI * carry);
    for (let i = 0; i < 4; i++) {
      const a = turn + i * TAU / 4 + NOTCH * .5, p = point(eq, a);
      if ((p[2] >= 0) !== front) continue;
      const s = size * (.9 + .12 * clamp(p[2], -1, 1)), alpha = base * (front ? 1 : .6);
      if (motion > .02) {
        for (let e = 2; e >= 1; e--) {
          const q = point(eq, a - e * NOTCH * .3);
          if ((q[2] >= 0) === front) vessel(c, alpha * motion * (.42 - e * .13), q[0], q[1], s, 0, 0, w, true);
        }
      }
      vessel(c, alpha, p[0], p[1], s, front ? glow : glow * .6, flash, w, false);
    }
  }

  function draw(context, bounds, time, opacity, front) {
    if (!(opacity > 0)) return;
    const c = context, [left, top, width, height] = bounds, t = Number.isFinite(time) ? time : 0;
    c.save();
    c.globalAlpha *= Math.min(1, opacity);
    const base = c.globalAlpha;
    if (!(base > 0)) { c.restore(); return; }
    c.lineCap = "round"; c.lineJoin = "round"; c.globalCompositeOperation = "source-over"; c.setLineDash([]);

    const cx = left + width * .5, w = clamp(height * .0075, 1.4, 3);
    const n = Math.floor(t / BEAT), ph = t / BEAT - n;
    const fy = top + height - 2, gry = clamp(height * .05, 8, 15);
    const g = { cx, fy, rx: width * .78, ry: gry, footY: fy + gry * .45 };
    const crownY = top + height * .01;

    const tilt = .26 * Math.sin(TAU * t / (BEAT * 4)), erx = width * .74, ery = height * .2;
    const equator = ring(cx, top + height * .56,
      [erx * Math.cos(tilt), erx * Math.sin(tilt), 0], [-ery * Math.sin(tilt), ery * Math.cos(tilt), 1]);
    // The meridian turns on a vertical axle from the ground cradle to a crown finial behind the head.
    const phi = .44 * Math.sin(TAU * t / (BEAT * 5) + 1), R = width * .56, H = (g.footY - crownY) / 2;
    const meridian = ring(cx, (g.footY + crownY) / 2, [R * Math.cos(phi), 0, Math.sin(phi)], [0, -H, -.35]);

    const dialA = -stepped(t, DIAL) * NOTCH, turn = stepped(t, CARRY) * NOTCH;
    const read = bump(ph, .12, .36), ign = bump(ph, .18, .4);
    const climb = (ph - CLIMB[0]) / (CLIMB[1] - CLIMB[0]);
    const carry = clamp((ph - CARRY[0]) / (CARRY[1] - CARRY[0]), 0, 1);
    const arrived = ages(t, CARRY[0])[0], glow = .3 + .7 * Math.exp(-arrived / 1.8), flash = Math.exp(-arrived / .45);
    const nodes = [crossing(meridian, equator, 1), crossing(meridian, equator, -1)].filter(Boolean);

    if (!front) ground(c, base, g, w, t, dialA, read, ign);
    // Meridian echoes spread sideways only, so its foot and crown stay seated and clear of the health bar.
    echoes(c, base, meridian, front, w * .8, ages(t, IGNITE), BEAT * 1.3, .1, 0, front ? .45 : .3);
    band(c, base, meridian, front, w * .85, front ? .8 : .45, 0);
    if (!front) finial(c, base, cx, crownY, w);
    pulse(c, base, meridian, front, nodes, climb, w);
    if (front) cradle(c, base, cx, g.footY, w, ign);
    echoes(c, base, equator, front, w * 1.6, ages(t, CARRY[0]), BEAT * 1.6, .13, .13, front ? .6 : .35);
    band(c, base, equator, front, w * 1.6, front ? .95 : .55, turn);
    charge(c, base, equator, front, nodes, (ph - CARRY[0]) / .3, w);
    collars(c, base, meridian, front, nodes, w);
    vessels(c, base, equator, front, turn, clamp(height * .045, 10, 17), w, glow, flash, carry);
    c.restore();
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies["opus-architect"] = {
    name: "Verdigris Escapement",
    description: "The Verdigris Armillary as one working instrument. The meridian stands in a bronze cradle on a copper-piped ground dial. Each beat the dial clicks a chalk glyph to the front, a blue spark becomes a lilac pulse that climbs the meridian, and the equator band clicks forward while its energy vessels charge and ring out an echo.",
    fit: "Uses the Architect's workshop: brass and copper pipework, chalk drafting marks, glowing energy vessels and a small spark of staff-blue flame.",
    tradeoff: "Motion follows a slow 4.4 second beat, so it is calmer and less constantly busy. The vessels and pulse are small details that soften at the smallest party scale.",
    implementation: "One shared beat clock drives the dial step, pulse climb, band step and echoes. Band crossings are solved per frame. Every part is split into back and front passes by depth.",
    draw
  };
})();
