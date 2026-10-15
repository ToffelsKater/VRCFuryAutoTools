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
