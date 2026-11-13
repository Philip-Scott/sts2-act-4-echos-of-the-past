(function () {
  "use strict";

  const TAU = Math.PI * 2;
  const INK = "#222e38";
  const CLAY = "#4d4350";
  const RUST = "#a56650";
  const noise = n => (Math.sin(n * 127.1 + 31.7) * 43758.5453) % 1;

  function point(ring, angle, radius = 1, drop = 0) {
    const x = Math.cos(angle) * ring.rx * radius;
    const y = Math.sin(angle) * ring.ry * radius;
    const c = Math.cos(ring.tilt), s = Math.sin(ring.tilt);
    return [ring.x + x * c - y * s, ring.y + x * s + y * c + drop];
  }

  function trace(ctx, points) {
    ctx.beginPath();
    points.forEach((p, i) => i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]));
    ctx.closePath();
  }

  function arc(ctx, ring, start, end, radius, drop = 0) {
    ctx.beginPath();
    for (let i = 0; i <= 12; i++) {
      const p = point(ring, start + (end - start) * i / 12, radius, drop);
      if (i) ctx.lineTo(p[0], p[1]);
      else ctx.moveTo(p[0], p[1]);
    }
  }

  function face(ctx, ring, start, end, inner, outer, seed, drop = 0) {
    const vertices = [];
    for (let i = 0; i <= 10; i++) {
      const t = i / 10;
      const chip = i === 1 || i === 8 ? Math.abs(noise(seed + i)) * .016 : 0;
      vertices.push(point(ring, start + (end - start) * t, outer - chip, drop));
    }
    for (let i = 10; i >= 0; i--) {
      const chip = i === 2 || i === 9 ? Math.abs(noise(seed - i)) * .014 : 0;
      vertices.push(point(ring, start + (end - start) * i / 10, inner + chip, drop));
    }
    trace(ctx, vertices);
  }

  function shard(ctx, ring, start, end, index) {
    const inner = 1 - ring.width;
    const outer = 1 + ring.width * .38;
    const inset = Math.min(.018, (end - start) * .12);
    const blue = index % 5 === 1 || index % 5 === 4;
    const middle = point(ring, (start + end) / 2);
    const paint = ctx.createLinearGradient(
      middle[0] - 12, middle[1] - 12, middle[0] + 8, middle[1] + 14
    );
    paint.addColorStop(0, blue ? "#829b99" : "#dfd9bb");
    paint.addColorStop(.4, blue ? "#536f75" : "#bbbba8");
    paint.addColorStop(1, blue ? "#344953" : "#798b8d");

    // The low clay edge makes a solid shard, rather than a luminous stroke.
    face(ctx, ring, start, end, inner, outer, index, ring.drop);
    ctx.fillStyle = CLAY;
    ctx.fill();
    ctx.strokeStyle = INK;
    ctx.lineWidth = .9;
    ctx.stroke();
    face(ctx, ring, start, end, inner, outer, index);
    ctx.fillStyle = paint;
    ctx.fill();

    face(ctx, ring, start + inset, end - inset, inner + ring.width * .2,
      inner + ring.width * .5, index);
    ctx.fillStyle = blue ? "#a3b2a1" : "#45616b";
    ctx.fill();

    arc(ctx, ring, start + inset, end - inset, outer - .008);
    ctx.strokeStyle = blue ? "#b6c1af" : "#e7dfc4";
    ctx.lineWidth = ring.floor ? .7 : 1.05;
    ctx.stroke();

    if (index % 3 === 0) {
      const a = start + (end - start) * .63;
      const p = point(ring, a, inner + ring.width * .1);
      const q = point(ring, a + .012, outer);
      ctx.beginPath();
      ctx.moveTo(p[0], p[1]);
      ctx.lineTo((p[0] + q[0]) / 2 - 1, (p[1] + q[1]) / 2);
      ctx.lineTo(q[0], q[1]);
      ctx.strokeStyle = RUST;
      ctx.lineWidth = ring.floor ? 1.4 : 2;
      ctx.stroke();
    }
  }

  function hoop(ctx, ring, time, front) {
    const step = TAU / ring.count;
    for (let i = 0; i < ring.count; i++) {
      const phase = ring.phase + time * ring.speed;
      const start = step * i + phase + .042;
      const end = start + step * (.70 + Math.abs(noise(i + 8)) * .15);
      const local = {
        ...ring,
        y: ring.y + (ring.floor ? 0 : Math.sin(time * .65 + i * 1.9) * 1.15)
      };
      if (ring.floor) {
        shard(ctx, local, start, end, i);
        continue;
      }

      // Split actual plates at the depth plane, including plates crossing it.
      const cuts = [start];
      const first = Math.floor((start + ring.depth) / Math.PI) + 1;
      for (let k = first; k * Math.PI - ring.depth < end; k++) {
        cuts.push(k * Math.PI - ring.depth);
      }
      cuts.push(end);
      for (let j = 0; j < cuts.length - 1; j++) {
        const a = cuts[j], b = cuts[j + 1];
        if ((Math.sin((a + b) / 2 + ring.depth) >= 0) === front) {
          shard(ctx, local, a, b, i);
        }
      }
    }
  }

  function floor(ctx, x, feet, width, height, time) {
    const rx = width * .67, ry = Math.min(16, height * .047);
    const ring = {
      x, y: feet - 2, rx, ry, tilt: 0, width: .15, drop: 2.4,
      count: 18, phase: .08, speed: 0, floor: true
    };
    ctx.save();
    const pool = ctx.createRadialGradient(x, feet - 2, 0, x, feet - 2, rx);
    pool.addColorStop(0, "rgba(35,47,63,0.05)");
    pool.addColorStop(.72, "rgba(46,66,79,0.28)");
    pool.addColorStop(1, "rgba(23,30,39,0)");
    ctx.beginPath();
    ctx.ellipse(x, feet - 2, rx, ry + 2, 0, 0, TAU);
    ctx.fillStyle = pool;
    ctx.fill();
    hoop(ctx, ring, time, false);

    // Four broad, matte ceramic keys repeat the body's red repair seams.
    for (let i = 0; i < 4; i++) {
      const angle = i * Math.PI / 2 + .32;
      const a = point(ring, angle, .72), b = point(ring, angle, 1.03);
      ctx.beginPath();
      ctx.moveTo(a[0], a[1]);
      ctx.lineTo(b[0], b[1]);
      ctx.strokeStyle = RUST;
      ctx.lineWidth = 2.1;
      ctx.stroke();
    }
    ctx.restore();
  }

  window.EchoCircleStudies ??= {};
  window.EchoCircleStudies.sol = {
    name: "Porcelain wake",
    description: "Chipped ivory and blue-enamel hoops float above a matching broken floor seal. Thick clay edges, rust-red repairs, and slowly drifting crescent plates replace glowing lines.",
    draw(context, bounds, time, opacity, front) {
      if (!(opacity > 0) || bounds[2] <= 0 || bounds[3] <= 0) return;
      const [left, top, width, height] = bounds;
      const x = left + width * .5;
      const scale = Math.min(width / 210, height / 320);
      context.save();
      try {
        context.globalAlpha *= Math.min(1, opacity);
        context.globalCompositeOperation = "source-over";
        context.shadowBlur = 0;
        context.shadowColor = "transparent";
        context.filter = "none";
        context.setLineDash([]);
        context.lineJoin = "round";
        context.lineCap = "butt";
        if (!front) floor(context, x, top + height, width, height, time);

        const upright = {
          x, y: top + height * .49, rx: width * .5, ry: height * .425,
          tilt: .16 + Math.sin(time * .22) * .045,
          width: .047, drop: 2.3 * scale, count: 10,
          phase: .17, speed: -.055, depth: -Math.PI / 2
        };
        const belt = {
          x, y: top + height * .59, rx: width * .65, ry: height * .128,
          tilt: -.13 + Math.sin(time * .27) * .055,
          width: .083, drop: 3.1 * scale, count: 13,
          phase: .31, speed: .09, depth: 0
        };
        // The narrow upright ring leaves the face open; the low bow binds the torso.
        hoop(context, upright, time, front);
        hoop(context, belt, time, front);
      } finally {
        context.restore();
      }
    }
  };
})();
