(() => {
  "use strict";

  // Throwaway study: a suspended bronze bell-frame above its buried sounding ring.
  const TAU = Math.PI * 2;
  const colors = {
    shadow: "#26271e",
    bronze: "#826138",
    edge: "#b89959",
    recess: "#463a25",
    patina: "#4c7260",
    patinaLight: "#789077",
    amber: "#9b592c",
    amberLight: "#c18a45"
  };

  function point(ring, angle, offset = 0) {
    const cosine = Math.cos(angle), sine = Math.sin(angle);
    const nx = cosine / ring.rx, ny = sine / ring.ry;
    const length = Math.hypot(nx, ny);
    const x = ring.rx * cosine + offset * nx / length;
    const y = ring.ry * sine + offset * ny / length;
    const tiltCos = Math.cos(ring.tilt), tiltSin = Math.sin(ring.tilt);
    return [
      ring.x + x * tiltCos - y * tiltSin,
      ring.y + x * tiltSin + y * tiltCos
    ];
  }

  function polygon(c, points, fill) {
    c.beginPath();
    c.moveTo(...points[0]);
    for (let i = 1; i < points.length; i++) c.lineTo(...points[i]);
    c.closePath();
    c.fillStyle = fill;
    c.fill();
  }

  function line(c, points, color, width) {
    c.beginPath();
    c.moveTo(...points[0]);
    for (let i = 1; i < points.length; i++) c.lineTo(...points[i]);
    c.strokeStyle = color;
    c.lineWidth = width;
    c.stroke();
  }

  // Split circles and hanging swags at the same depth plane as their attachments.
  function slices(start, end, depth, front, draw) {
    const cuts = [start, end];
    const first = Math.ceil((start + depth) / Math.PI);
    const last = Math.floor((end + depth) / Math.PI);
    for (let i = first; i <= last; i++) {
      const cut = i * Math.PI - depth;
      if (cut > start && cut < end) cuts.push(cut);
    }
    cuts.sort((a, b) => a - b);
    for (let i = 0; i < cuts.length - 1; i++) {
      const a = cuts[i], b = cuts[i + 1];
      if ((Math.sin((a + b) / 2 + depth) > 0) === front) draw(a, b);
    }
  }

  function arc(ring, start, end, offset = 0) {
    const points = [], count = Math.max(2, Math.ceil((end - start) * 15));
    for (let i = 0; i <= count; i++) {
      points.push(point(ring, start + (end - start) * i / count, offset));
    }
    return points;
  }

  function frame(c, ring, width, front) {
    slices(0, TAU, ring.depth, front, (a, b) => {
      const points = arc(ring, a, b);
      line(c, points.map(([x, y]) => [x, y + 1.5]), colors.shadow, width + 2);
      line(c, points, colors.bronze, width);
      line(c, arc(ring, a, b, -width * 0.25), colors.recess, width * 0.28);
    });
    for (let i = 0; i < 8; i++) {
      const a = i * TAU / 8 + 0.06;
      slices(a, a + 0.36, ring.depth, front, (start, end) => {
        line(c, arc(ring, start, end, width * 0.2),
          i % 3 === 0 ? colors.edge : colors.patina, width * 0.45);
      });
    }
  }

  function chime(c, ring, angle, scale, time, index, front) {
    if ((Math.sin(angle + ring.depth) > 0) !== front) return;
    const [x, y] = point(ring, angle);
    c.save();
    c.translate(x, y);
    c.scale(scale, scale);
    c.rotate(Math.sin(time * 1.05 - index * 0.8) * 0.12);
    const chain = 10 + (index % 3) * 4;
    for (let y = 0; y < chain; y += 4) {
      c.beginPath();
      c.ellipse(0, y + 2, y % 8 === 0 ? 1.7 : 0.8, 2.7, 0, 0, TAU);
      c.strokeStyle = colors.edge;
      c.lineWidth = 1.3;
      c.stroke();
    }
    c.translate(0, chain);
    const metal = c.createLinearGradient(-8, 0, 9, 13);
    metal.addColorStop(0, colors.amberLight);
    metal.addColorStop(0.3, colors.amber);
    metal.addColorStop(1, colors.recess);
    c.beginPath();
    c.moveTo(-2, 0);
    c.bezierCurveTo(-6, 1, -4, 10, -9, 14);
    c.quadraticCurveTo(0, 18, 9, 14);
    c.bezierCurveTo(4, 10, 6, 1, 2, 0);
    c.closePath();
    c.fillStyle = metal;
    c.fill();
    c.strokeStyle = colors.shadow;
    c.lineWidth = 1.2;
    c.stroke();
    line(c, [[-3, 2], [-3, 6], [-5, 11]], colors.amberLight, 1.2);
    line(c, [[-3, 1], [3, 1]], colors.patinaLight, 2.4);
    c.beginPath();
    c.ellipse(0, 14, 7, 2.1, 0, 0, TAU);
    c.fillStyle = colors.shadow;
    c.fill();
    line(c, [[0, 14], [Math.sin(time * 1.05 - index * 0.8) * 2, 20]],
      colors.bronze, 2);
    line(c, [[-7, 15], [-2, 16]], colors.edge, 1.1);
    c.restore();
  }

  function swags(c, ring, phase, scale, front) {
    for (let i = 0; i < 6; i++) {
      const start = phase + i * TAU / 6, end = start + TAU / 6;
      slices(start, end, ring.depth, front, (a, b) => {
        const points = [];
        for (let j = 0; j <= 12; j++) {
          const angle = a + (b - a) * j / 12;
          const [x, y] = point(ring, angle);
          points.push([x, y + Math.sin((angle - start) / (end - start) * Math.PI) * 11 * scale]);
        }
        line(c, points, colors.shadow, 2.5 * scale);
        line(c, points, colors.bronze, 1.3 * scale);
      });
    }
  }

  function floor(c, x, feet, width, height, time) {
    const rx = width * 0.72, ry = Math.min(15, height * 0.045);
    const y = feet - 3;
    c.save();
    c.translate(x, y);
    c.scale(1, (ry + 3) / rx);
    const shadow = c.createRadialGradient(0, 0, 0, 0, 0, rx);
    shadow.addColorStop(0, "rgba(24,29,24,0.04)");
    shadow.addColorStop(0.66, "rgba(24,29,24,0.34)");
    shadow.addColorStop(1, "rgba(24,29,24,0)");
    c.beginPath();
    c.ellipse(0, 0, rx, rx, 0, 0, TAU);
    c.fillStyle = shadow;
    c.fill();
    c.restore();

    const ring = { x, y, rx, ry, tilt: 0 };
    const inner = { ...ring, rx: rx * 0.83, ry: ry * 0.83 };
    const hull = arc(ring, 0, TAU).concat(arc(inner, 0, TAU).reverse());
    polygon(c, hull.map(([px, py]) => [px, py + 2]), colors.shadow);
    polygon(c, hull, colors.bronze);
    line(c, arc(inner, 0, TAU), colors.recess, 1.7);
    for (let i = 0; i < 8; i++) {
      const a = i * TAU / 8;
      line(c, arc(ring, a, a + 0.42), i % 3 === 0 ? colors.edge : colors.patina, 2);
      const foot = { ...ring, rx: rx + 5, ry: ry + 2 };
      polygon(c, [point(inner, a - 0.06), point(foot, a - 0.035),
        point(foot, a + 0.035), point(inner, a + 0.06)], colors.recess);
    }
    // The buried ring answers the bells with a quiet, shallow expanding echo.
    const pulse = (Math.sin(time * 0.65) + 1) / 2;
    const echo = { ...ring, rx: rx * (0.56 + pulse * 0.12), ry: ry * (0.56 + pulse * 0.12) };
    c.save();
    c.globalAlpha *= (1 - pulse) * 0.36;
    line(c, arc(echo, 0, TAU), colors.patinaLight, 1.8);
    c.restore();
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies.astra = {
    name: "Hollow chimes",
    description: "Weathered bronze circles form a suspended bell-frame with drooping chains and dark amber bells. The bells swing out of step above a buried, verdigris-stained sounding ring.",
    draw(context, bounds, time, opacity, front) {
      if (opacity <= 0) return;
      const [left, top, width, height] = bounds;
      const c = context, x = left + width * 0.5;
      c.save();
      c.globalAlpha *= opacity;
      c.globalCompositeOperation = "source-over";
      c.setLineDash([]);
      c.lineCap = "round";
      c.lineJoin = "round";
      c.shadowBlur = 0;
      c.shadowOffsetX = 0;
      c.shadowOffsetY = 0;
      c.shadowColor = "transparent";
      c.filter = "none";
      if (!front) floor(c, x, top + height, width, height, time);
      const upright = {
        x, y: top + height * 0.47, rx: width * 0.44, ry: height * 0.45,
        tilt: -0.08 + Math.sin(time * 0.23) * 0.035, depth: Math.PI / 2
      };
      const waist = {
        x, y: top + height * 0.46, rx: width * 0.65, ry: height * 0.12,
        tilt: 0.08 + Math.sin(time * 0.3) * 0.04, depth: 0
      };
      frame(c, upright, width * 0.022, front);
      const phase = time * 0.065 + 0.28;
      swags(c, waist, phase, width / 210, front);
      frame(c, waist, width * 0.027, front);
      for (let i = 0; i < 6; i++) {
        chime(c, waist, phase + i * TAU / 6, width / 210, time, i, front);
      }
      c.restore();
    }
  };
})();
