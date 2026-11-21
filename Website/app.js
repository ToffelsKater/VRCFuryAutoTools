// No Scriban here on purpose: all dynamic data lives in index.html (data-listing-url).
const listingUrl = document.body.dataset.listingUrl;

document.getElementById('addToVcc').addEventListener('click', () => {
  window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`);
});

const copyButton = document.getElementById('copyUrl');
const copyLabel = copyButton.querySelector('span');
copyButton.addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(listingUrl);
    copyLabel.textContent = 'Copied';
  } catch {
    // Clipboard API blocked: select the URL so Ctrl+C works.
    getSelection().selectAllChildren(document.getElementById('listingUrl'));
    copyLabel.textContent = 'Press Ctrl+C';
  }
  copyButton.classList.add('copied');
  setTimeout(() => {
    copyLabel.textContent = 'Copy';
    copyButton.classList.remove('copied');
  }, 1600);
});

const emptyState = document.getElementById('emptyState');
document.getElementById('search').addEventListener('input', ({ target: { value } }) => {
  const q = value.trim().toLowerCase();
  let shown = 0;
  document.querySelectorAll('#packageList .package').forEach(p => {
    p.hidden = q !== '' && !p.dataset.search.toLowerCase().includes(q);
    if (!p.hidden) shown++;
  });
  emptyState.hidden = shown > 0;
});

// Tool diagrams: each [data-viz] tile loops a short animation; captions follow the steps.
(() => {
  const cl = x => x < 0 ? 0 : x > 1 ? 1 : x;
  const eo = x => 1 - Math.pow(1 - cl(x), 3);
  const eio = x => { x = cl(x); return x < .5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; };
  const P = (t, a, b, e = eo) => e((t - a) / (b - a));
  const bump = (t, c, w) => { const d = Math.abs(t - c) / w; return d >= 1 ? 0 : 1 - d * d; };
  const mix = (k, a = 'var(--accent)', b = 'var(--muted)') => `color-mix(in oklab, ${a} ${(cl(k) * 100).toFixed(1)}%, ${b})`;
  const at = (el, cx, cy, s) => el.setAttribute('transform', `translate(${cx} ${cy}) scale(${s})`);
  const op = (el, v) => { el.style.opacity = cl(v).toFixed(3); };
  const lerp = (a, b, k) => a + (b - a) * k;
  // Rounded path through points: corners are cut between the midpoints of the segments, the ends are exact.
  const smooth = p => p.map(([x, y], i) => {
    const mid = i < p.length - 1 ? `${((x + p[i + 1][0]) / 2).toFixed(1)} ${((y + p[i + 1][1]) / 2).toFixed(1)}` : '';
    return i === 0 ? `M${x.toFixed(1)} ${y.toFixed(1)}L${mid}` : mid ? `Q${x.toFixed(1)} ${y.toFixed(1)} ${mid}` : `L${x.toFixed(1)} ${y.toFixed(1)}`;
  }).join('');

  const tiles = {
    quest: {
      dur: 8.5, still: 6,
      steps: [[0, 'Your PC avatar'], [.4, 'A copy appears next to it'], [2.4, 'Materials get a mobile shader'], [3.2, 'Big textures are capped at 1024'], [4, 'Unity constraints become VRC Constraints'], [5, 'Ready to upload for Android'], [7.4, 'Your PC avatar']],
      render(t, q) {
        const c = P(t, .4, 1.6, eio), out = 1 - P(t, 7.4, 8.1);
        q('copy')[0].setAttribute('transform', `translate(${(-204 * (1 - c)).toFixed(2)} 0)`);
        op(q('copy')[0], Math.min(1, c * 3) * out);
        op(q('arrow')[0], P(t, 1, 1.6) * out);
        const a = P(t, 1.8, 2.3);
        q('qa').forEach(e => e.style.stroke = mix(a));
        op(q('h0')[0], 1 - a); op(q('h1')[0], a);
        q('o').forEach((e, i) => {
          const s = P(t, 2.6 + i * .8, 3.1 + i * .8);
          op(e, 1 - s); e.setAttribute('transform', `translate(0 ${(-5 * s).toFixed(2)})`);
          const n = q('n')[i]; op(n, s); n.setAttribute('transform', `translate(0 ${(5 * (1 - s)).toFixed(2)})`);
          op(q('bar')[i], bump(t, 2.85 + i * .8, .6));
        });
      }
    },
    outfit: {
      dur: 8, still: 3.6,
      steps: [[0, 'An outfit with three meshes'], [.8, 'Each mesh gets a VRCFury Toggle'], [2.9, 'Sorted under Outfits/OutfitXY'], [4, 'Jeans switched off in game'], [5.4, 'And back on'], [7.2, 'An outfit with three meshes']],
      render(t, q) {
        const fo = 1 - P(t, 7.2, 7.8);
        op(q('mh')[0], P(t, .2, .6) * fo);
        const off = P(t, 4, 4.3) * (1 - P(t, 5.4, 5.7));
        q('r').forEach((r, i) => {
          const b = .8 + i * .6;
          q('cr')[i].setAttribute('width', (52 * P(t, b, b + .35) * fo).toFixed(2));
          const ri = P(t, b + .25, b + .6);
          op(r, ri * fo); r.setAttribute('transform', `translate(${(8 * (1 - ri)).toFixed(2)} 0)`);
          let k = P(t, b + .6, b + .9);
          if (i === 1) k *= 1 - off;
          op(q('ton')[i], k);
          const kn = q('kn')[i]; kn.setAttribute('cx', (263 + 12 * k).toFixed(2)); kn.style.fill = mix(k);
        });
        op(q('m')[1], 1 - .65 * off);
      }
    },
    zero: {
      dur: 8, still: 5.5,
      steps: [[0, 'An armature with 9 bones'], [.6, 'Checking mesh weights on every bone'], [2.6, '3 bones move nothing'], [3.3, 'Removed when you upload'], [4.8, '6 bones left, originals unchanged'], [7, 'An armature with 9 bones']],
      render(t, q) {
        const pass = y => .6 + (y / 140) * 1.8;
        const sy = 140 * P(t, .6, 2.4, x => cl(x));
        const sOn = Math.min(P(t, .5, .7), 1 - P(t, 2.3, 2.6));
        const scan = q('scan')[0]; scan.setAttribute('y1', sy); scan.setAttribute('y2', sy); op(scan, sOn * .8);
        const band = q('band')[0]; band.setAttribute('y', sy - 18); op(band, sOn);
        const restore = P(t, 7, 7.6);
        let removed = 0;
        q('n').forEach(n => { n.style.stroke = mix(bump(t, pass(+n.getAttribute('cy')), .3)); });
        q('g').forEach((g, i) => {
          const cx = +g.getAttribute('cx'), cy = +g.getAttribute('cy');
          const x = P(t, 2.6 + i * .25, 2.9 + i * .25, eio);
          const f = P(t, 3.3 + i * .45, 3.7 + i * .45);
          if (t >= 3.7 + i * .45 && t < 7) removed++;
          const marked = x > 0 && t < 7;
          g.style.stroke = marked ? 'var(--muted)' : mix(bump(t, pass(cy), .3));
          g.style.strokeDasharray = marked ? '3 4' : '';
          const vis = t < 7 ? 1 - f : restore;
          op(g, vis); op(q('gl')[i], vis);
          q('gl')[i].style.strokeDasharray = marked ? '3 4' : '';
          const gx = q('gx')[i]; at(gx, cx, cy, t < 7 ? x : 0); op(gx, t < 7 ? 1 - f : 0);
        });
        q('cnt')[0].textContent = `${9 - removed} bones` + (removed ? ` · ${removed} removed` : '');
      }
    },
    strip: {
      dur: 8.6, still: 6,
      steps: [[0, 'Bones from an outfit armature'], [.5, 'Hips to Head are kept by default'], [2, 'Body mesh is in the list'], [2.5, 'Bones it is weighted to stay'], [3.7, 'Every other bone is removed'], [7.6, 'Bones from an outfit armature']],
      render(t, q) {
        const r = 1 - P(t, 7.6, 8.2);
        q('core')[0].style.strokeDashoffset = (1 - P(t, .5, 1.7, x => cl(x))) .toFixed(3);
        op(q('core')[0], r);
        q('sq').forEach((s, i) => {
          const k = P(t, .5 + i * .24, .75 + i * .24) * r;
          s.style.stroke = mix(k); s.style.fill = mix(k * .14, 'var(--accent)', 'var(--surface)');
          q('sl')[i].style.fill = mix(k);
        });
        const pk = P(t, 2, 2.4) * r;
        q('pill')[0].style.stroke = mix(pk); q('pill')[0].style.fill = mix(pk * .14, 'var(--accent)', 'var(--surface)');
        q('pt')[0].style.fill = mix(pk);
        q('w').forEach((w, i) => {
          const a = 2.5 + i * .22, k = P(t, a, a + .25) * r;
          w.style.stroke = mix(k); w.style.fill = mix(k * .2, 'var(--accent)', 'var(--surface)');
          const ring = q('wr')[i], rp = P(t, a, a + .7, x => cl(x));
          ring.setAttribute('r', (7 + 9 * rp).toFixed(2)); op(ring, rp > 0 && rp < 1 ? 1 - rp : 0);
        });
        const restore = P(t, 7.6, 8.2);
        const fades = q('g').map((g, i) => {
          const cx = +g.getAttribute('cx'), cy = +g.getAttribute('cy');
          const x = P(t, 3.7 + i * .15, 4 + i * .15, eio), f = P(t, 4.3 + i * .3, 4.7 + i * .3);
          const marked = x > 0 && t < 7.6;
          g.style.strokeDasharray = marked ? '3 4' : '';
          const vis = t < 7.6 ? 1 - f : restore;
          op(g, vis);
          const gx = q('gx')[i]; at(gx, cx, cy, t < 7.6 ? x : 0); op(gx, t < 7.6 ? 1 - f : 0);
          return [vis, marked];
        });
        [0, 2, 3].forEach((gi, li) => { const l = q('gl')[li]; op(l, fades[gi][0]); l.style.strokeDasharray = fades[gi][1] ? '3 4' : ''; });
      }
    },
    coll: {
      dur: 8.6, still: 6.4,
      steps: [[0, 'Avatar skeleton with a tail'], [.5, 'Component added: every humanoid bone gets a collider'], [2.8, 'On the tail: that bone and all its children'], [4.4, 'Radius scale raised, colliders regenerated'], [7.6, 'Avatar skeleton with a tail']],
      render(t, q) {
        const fo = 1 - P(t, 7.6, 8.2);
        const rs = 1 + .3 * P(t, 4.6, 5.4, eio);
        const h = P(t, .5, .9, eio), hc = q('hc')[0];
        hc.setAttribute('r', (14 * rs * h).toFixed(2)); op(hc, fo);
        q('cap').forEach(c => {
          const a = +c.dataset.a, p = P(t, a, a + .4);
          c.style.strokeDasharray = '1 2'; c.style.strokeDashoffset = (1 - p).toFixed(3);
          c.style.strokeWidth = (+c.dataset.w * rs).toFixed(2);
          op(c, Math.min(1, p * 4) * fo);
        });
        q('tl')[0].style.fill = mix(P(t, 2.8, 3.1) * fo);
        const rad = q('rad')[0];
        rad.textContent = `Radius ×${rs.toFixed(1)}`;
        rad.style.fill = mix(bump(t, 5, .9));
      }
    },
    digi: {
      dur: 9.4, still: 5.6,
      steps: [[0, 'Humanoid leg and fake digitigrade leg'], [.5, 'VRChat only moves the humanoid leg'], [2.2, 'At build the fake foot is pinned to the real one'], [3, 'Now the fake leg follows every step'], [4.8, 'Hinge limits keep knee and hock bending right'], [6.8, 'No Final IK? VRC constraints instead'], [8.6, 'Humanoid leg and fake digitigrade leg']],
      render(t, q) {
        const lift = a => P(t, a, a + .7, eio) * (1 - P(t, a + .8, a + 1.5, eio));
        const s = lift(.5) + lift(3) + lift(4.8) + lift(6.9);
        const bound = P(t, 2.2, 2.6) * (1 - P(t, 8.6, 9.2));
        const H = [190, 16], rest = [186, 128], F = [186 + 30 * s, 128 - 44 * s];
        // direction of the first bone of a two-bone leg that bends forward
        const dir = (f, l1, l2) => {
          const dx = f[0] - H[0], dy = f[1] - H[1], d = Math.min(Math.hypot(dx, dy), l1 + l2 - .01);
          const a = Math.atan2(dy, dx) - Math.acos((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d));
          return [Math.cos(a), Math.sin(a)];
        };
        const pt = p => `${p[0].toFixed(1)} ${p[1].toFixed(1)}`;
        const put = (el, p) => { el.setAttribute('cx', p[0].toFixed(1)); el.setAttribute('cy', p[1].toFixed(1)); };

        const ru = dir(F, 60, 60), rk = [H[0] + 60 * ru[0], H[1] + 60 * ru[1]];
        q('rl')[0].setAttribute('d', `M${pt(H)}L${pt(rk)}L${pt(F)}h12`);
        put(q('rj')[0], rk);

        // thigh 40, shin 58, hock 36: the hock stays parallel to the thigh, so it solves like a two-bone leg of 76 and 58
        const ff = t < 2.2 ? rest : F;
        const u = dir(ff, 76, 58), knee = [H[0] + 40 * u[0], H[1] + 40 * u[1]], hock = [ff[0] - 36 * u[0], ff[1] - 36 * u[1]];
        const leg = q('fk')[0];
        leg.setAttribute('d', `M${pt(H)}L${pt(knee)}L${pt(hock)}L${pt(ff)}h14`);
        leg.style.stroke = mix(bound);
        q('lg')[0].style.stroke = mix(bound); q('lt')[0].style.fill = mix(bound);
        const sw = P(t, 6.8, 7.2), hinge = P(t, 4.8, 5.2) * (1 - sw);
        [knee, hock].forEach((p, i) => {
          put(q('fj')[i], p); q('fj')[i].style.fill = mix(bound);
          put(q('hg')[i], p); op(q('hg')[i], hinge);
        });
        const ring = q('tg')[0], rp = P(t, 2.2, 2.9, x => cl(x));
        put(ring, F); ring.setAttribute('r', (4 + 12 * rp).toFixed(2)); op(ring, rp > 0 && rp < 1 ? 1 - rp : 0);
        put(q('td')[0], F); op(q('td')[0], bound);
        op(q('m0')[0], bound * (1 - sw)); op(q('m1')[0], bound * sw);
      }
    },
    fit: {
      dur: 9.4, still: 7.4,
      steps: [[0, 'An outfit made for another avatar'], [.8, 'Its bones move onto your avatar'], [2.2, 'Clipping is pushed out of the body'], [3.6, 'Gaps are closed, tight parts stay tight'], [5, 'Loose parts like skirts keep their shape'], [6.2, 'Edit fit: paint fixes on top'], [8.4, 'An outfit made for another avatar']],
      render(t, q) {
        const r = 1 - P(t, 8.4, 9, eio);
        const move = P(t, .8, 1.8, eio) * r, push = P(t, 2.2, 3.2, eio) * r, close = P(t, 3.6, 4.6, eio) * r;
        const paint = P(t, 6.6, 7.8, x => cl(x));
        // half widths at shoulder, chest, waist, hip and hem: the hem never changes
        const w = [lerp(30, 37, push), lerp(24, 33, push), lerp(34, 25, close), lerp(40, 37, close), 54];
        const ys = [42, 66, 96, 124, 156];
        const ox = 12 * (1 - move), oy = -9 * (1 - move);
        // the brush pushes the left waist out a little
        const side = d => ys.map((y, i) => [265 + ox + d * (w[i] + (d < 0 && i === 2 ? 4 * P(t, 7, 7.6) * r : 0)), y + oy]);
        q('fo')[0].setAttribute('d', smooth(side(1)) + smooth(side(-1).reverse()).replace('M', 'L') + 'Z');
        [.8, 2.2, 3.6, 5, 6.2].forEach((a, i) => {
          q('fl')[i].style.fill = mix(P(t, a, a + .3) * r);
          op(q('bar')[i], bump(t, a + .25, .6));
        });
        op(q('sk')[0], bump(t, 5.6, .7));
        const brush = q('br')[0];
        brush.setAttribute('cx', lerp(222, 240, paint).toFixed(1)); brush.setAttribute('cy', lerp(126, 84, paint).toFixed(1));
        op(brush, Math.min(P(t, 6.2, 6.6), 1 - P(t, 7.9, 8.3)));
      }
    },
    deci: {
      dur: 9, still: 6.4,
      steps: [[0, 'An avatar with 120,000 triangles'], [.6, 'Target: 70,000 for PC'], [1.4, 'At upload every mesh keeps the same share'], [3.4, 'The face with visemes stays as it is'], [4.4, 'Open edges and UV seams stay in place'], [5.6, '70,000 triangles, mesh files unchanged'], [8, 'An avatar with 120,000 triangles']],
      render(t, q) {
        const r = 1 - P(t, 8, 8.6, eio);
        const red = P(t, 1.4, 3.2, eio) * r, n = 120000 - 50000 * red;
        q('cnt')[0].textContent = (Math.round(n / 100) * 100).toLocaleString('en-US');
        q('fill')[0].setAttribute('width', (168 * n / 120000).toFixed(1));
        op(q('fine')[0], 1 - red);
        const face = bump(t, 3.9, .8);
        q('face')[0].style.stroke = mix(face); q('ft')[0].style.fill = mix(face);
        op(q('bd')[0], bump(t, 4.9, .8)); op(q('seam')[0], bump(t, 4.9, .8));
        [1.4, 3.4, 4.4, 5.6].forEach((a, i) => {
          q('fl')[i].style.fill = mix(P(t, a, a + .3) * r);
          op(q('bar')[i], bump(t, a + .25, .6));
        });
      }
    },
    poi: {
      dur: 8.6, still: 5.4,
      steps: [[0, 'One component per Poiyomi feature'], [.4, 'At upload: a Shader menu folder'], [2, 'LTCGI switched on in game'], [2.8, 'Intensity radial turned up'], [4.4, 'Every material with LTCGI follows'], [7.6, 'One component per Poiyomi feature']],
      render(t, q) {
        const r = 1 - P(t, 7.6, 8.2);
        op(q('mh')[0], P(t, .2, .5) * r);
        q('r').forEach((e, i) => {
          const k = P(t, .4 + i * .3, .75 + i * .3) * r;
          op(e, k); e.setAttribute('transform', `translate(${(8 * (1 - k)).toFixed(2)} 0)`);
        });
        const on = P(t, 2, 2.3) * r;
        q('ton').forEach((e, i) => op(e, i ? 0 : on));
        q('kn').forEach((e, i) => { const k = i ? 0 : on; e.setAttribute('cx', (153 + 12 * k).toFixed(2)); e.style.fill = mix(k); });
        const v = .8 * P(t, 2.8, 4, eio) * r, rad = q('rad')[0];
        rad.style.strokeDashoffset = (1 - v).toFixed(3); op(rad, v > .005 ? 1 : 0);
        q('pct')[0].textContent = `${Math.round(v * 100)}%`;
        q('mat').forEach((m, i) => {
          const k = P(t, 4.4 + i * .25, 4.8 + i * .25) * r;
          m.style.stroke = mix(k); m.style.fill = mix(k * .3, 'var(--accent)', 'var(--surface)');
        });
      }
    }
  };

  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const live = [];
  document.querySelectorAll('[data-viz]').forEach(root => {
    const def = tiles[root.dataset.viz]; if (!def) return;
    const cache = {};
    const q = k => cache[k] || (cache[k] = [...root.querySelectorAll(`[data-k="${k}"]`)]);
    const cap = root.querySelector('.step .t');
    const item = { def, q, cap, last: null, visible: true };
    item.frame = t => {
      def.render(t, q);
      let s = def.steps[0][1];
      for (const [st, txt] of def.steps) if (t >= st) s = txt;
      if (s !== item.last) { item.last = s; cap.textContent = s; cap.classList.remove('in'); void cap.offsetWidth; cap.classList.add('in'); }
    };
    item.frame(reduce ? def.still : 0);
    if (reduce) return;
    new IntersectionObserver(([e]) => { item.visible = e.isIntersecting; }).observe(root);
    live.push(item);
  });
  if (reduce || !live.length) return;
  const t0 = performance.now();
  const loop = now => {
    const s = (now - t0) / 1000;
    for (const it of live) if (it.visible) it.frame(s % it.def.dur);
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
})();
