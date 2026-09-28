// Manifold docs runtime config, read by the docfx "modern" template.
// `defaultTheme` is applied whenever a reader has no `theme` entry in
// localStorage yet (first visit): the void is the intended first
// impression, not the system light/dark guess. The stock light/dark/auto
// switch (and whatever a returning reader already chose) still works.
export default {
  defaultTheme: 'dark',
};

const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

// --------------------------------------------------------------------------
// Hover tilt for any .mf-tilt element (cards, islands, badges). Delegated
// and rAF-throttled so it costs nothing on pages that don't use the class.
// --------------------------------------------------------------------------
function initTilt() {
  if (reducedMotion()) return;
  const MAX_DEG = 6;
  let pending = null;

  document.addEventListener(
    'pointermove',
    (event) => {
      const el = event.target.closest && event.target.closest('.mf-tilt');
      if (!el || pending) return;
      pending = requestAnimationFrame(() => {
        pending = null;
        const rect = el.getBoundingClientRect();
        const px = (event.clientX - rect.left) / rect.width - 0.5;
        const py = (event.clientY - rect.top) / rect.height - 0.5;
        el.style.setProperty('--mf-tilt-x', `${(-py * MAX_DEG).toFixed(2)}deg`);
        el.style.setProperty('--mf-tilt-y', `${(px * MAX_DEG).toFixed(2)}deg`);
      });
    },
    { passive: true },
  );

  // pointerleave does not bubble, so this needs the capture phase to work
  // as event delegation.
  document.addEventListener(
    'pointerleave',
    (event) => {
      const el = event.target.closest && event.target.closest('.mf-tilt');
      if (!el) return;
      el.style.removeProperty('--mf-tilt-x');
      el.style.removeProperty('--mf-tilt-y');
    },
    true,
  );
}

// --------------------------------------------------------------------------
// Theme switch = dimension switch. The stock theme dropdown (docfx.min.js)
// still owns the actual switch: it sets data-bs-theme on click, this only
// wraps that flip in a View Transition so the swirl in main.css plays where
// supported. Nothing here calls preventDefault or stopPropagation, so the
// real switch keeps working unchanged if this feature is unavailable or
// throws.
// --------------------------------------------------------------------------
function initThemeSwirl() {
  if (reducedMotion() || typeof document.startViewTransition !== 'function') return;

  document.addEventListener(
    'click',
    (event) => {
      const item = event.target.closest && event.target.closest('.dropdown-item');
      if (!item || !item.querySelector('i.bi-sun, i.bi-moon, i.bi-circle-half')) return;

      document.startViewTransition(
        () =>
          new Promise((resolve) => {
            const observer = new MutationObserver(() => {
              observer.disconnect();
              resolve();
            });
            observer.observe(document.documentElement, {
              attributes: true,
              attributeFilter: ['data-bs-theme'],
            });
            // The reader may pick the theme that is already active, which
            // never mutates the attribute; don't hang the transition on it.
            setTimeout(resolve, 250);
          }),
      );
    },
    true,
  );
}

const touchDevice = () => window.matchMedia('(hover: none)').matches;

// --------------------------------------------------------------------------
// Hero parallax. Writes --mf-hero-dx/--mf-hero-dy on .mf-hero__stage from
// pointer offset (within the hero) and scroll progress (as the hero moves
// through the viewport); each .mf-hero__layer multiplies that by its own
// --mf-depth in main.css, so the layers drift at different rates. Off under
// reduced motion and on touch devices — the still <picture> fallback is the
// whole experience there.
// --------------------------------------------------------------------------
function initHeroParallax() {
  const stage = document.querySelector('.mf-hero__stage');
  if (!stage || reducedMotion() || touchDevice()) return;

  const POINTER_PX = 14;
  const SCROLL_PX = 36;
  let px = 0;
  let py = 0;
  let sy = 0;
  let pending = null;

  function apply() {
    pending = null;
    stage.style.setProperty('--mf-hero-dx', `${(px * POINTER_PX).toFixed(1)}px`);
    stage.style.setProperty('--mf-hero-dy', `${(py * POINTER_PX + sy * SCROLL_PX).toFixed(1)}px`);
  }
  function schedule() {
    if (!pending) pending = requestAnimationFrame(apply);
  }

  stage.addEventListener(
    'pointermove',
    (event) => {
      const rect = stage.getBoundingClientRect();
      px = (event.clientX - rect.left) / rect.width - 0.5;
      py = (event.clientY - rect.top) / rect.height - 0.5;
      schedule();
    },
    { passive: true },
  );
  stage.addEventListener(
    'pointerleave',
    () => {
      px = 0;
      py = 0;
      schedule();
    },
    { passive: true },
  );

  function onScroll() {
    const rect = stage.getBoundingClientRect();
    const vh = window.innerHeight || 1;
    sy = Math.max(-1, Math.min(1, (vh / 2 - (rect.top + rect.height / 2)) / vh));
    schedule();
  }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
}

// --------------------------------------------------------------------------
// Hero spark trail — a tiny voxel-spark canvas that only draws while the
// pointer is over the hero. Bounded particle count, and the rAF loop stops
// itself once the trail has fully faded and the pointer has left. Off under
// reduced motion and on touch devices.
// --------------------------------------------------------------------------
function initHeroSparks() {
  const stage = document.querySelector('.mf-hero__stage');
  const canvas = document.getElementById('mf-hero-sparks');
  if (!stage || !canvas || reducedMotion() || touchDevice()) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  let w = 0;
  let h = 0;
  function resize() {
    const rect = stage.getBoundingClientRect();
    w = canvas.width = Math.round(rect.width * dpr);
    h = canvas.height = Math.round(rect.height * dpr);
  }
  resize();
  window.addEventListener('resize', resize, { passive: true });

  const sparks = [];
  const MAX_SPARKS = 40;
  let raf = null;
  let hovering = false;

  function spawn(x, y) {
    if (sparks.length >= MAX_SPARKS) sparks.shift();
    sparks.push({
      x: x * dpr,
      y: y * dpr,
      life: 1,
      size: (2 + Math.random() * 2) * dpr,
      vx: (Math.random() - 0.5) * 0.3 * dpr,
      vy: (-0.2 - Math.random() * 0.3) * dpr,
    });
  }
  function tick() {
    ctx.clearRect(0, 0, w, h);
    for (let i = sparks.length - 1; i >= 0; i--) {
      const s = sparks[i];
      s.life -= 0.025;
      if (s.life <= 0) {
        sparks.splice(i, 1);
        continue;
      }
      s.x += s.vx;
      s.y += s.vy;
      ctx.globalAlpha = Math.max(0, s.life);
      ctx.fillStyle = '#e2a191';
      ctx.fillRect(s.x, s.y, s.size, s.size);
    }
    ctx.globalAlpha = 1;
    raf = sparks.length || hovering ? requestAnimationFrame(tick) : null;
  }
  function schedule() {
    if (!raf) raf = requestAnimationFrame(tick);
  }

  stage.addEventListener(
    'pointermove',
    (event) => {
      hovering = true;
      const rect = stage.getBoundingClientRect();
      spawn(event.clientX - rect.left, event.clientY - rect.top);
      schedule();
    },
    { passive: true },
  );
  stage.addEventListener('pointerleave', () => { hovering = false; }, { passive: true });
}

// --------------------------------------------------------------------------
// Section headings build themselves — appends a row of blocks after every
// .mf-build-heading and reveals them, staggered, the first time the
// heading scrolls into view. The blocks are appended unconditionally (they
// read as a static underline); only the reveal animation is gated on
// reduced motion.
// --------------------------------------------------------------------------
function initBuildHeadings() {
  const headings = document.querySelectorAll('.mf-build-heading');
  if (!headings.length) return;

  headings.forEach((heading) => {
    if (heading.querySelector('.mf-build-heading__blocks')) return;
    const row = document.createElement('span');
    row.className = 'mf-build-heading__blocks';
    row.setAttribute('aria-hidden', 'true');
    for (let i = 0; i < 7; i++) row.appendChild(document.createElement('span'));
    heading.appendChild(row);
  });

  if (reducedMotion() || typeof IntersectionObserver !== 'function') return;

  const io = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-visible');
        io.unobserve(entry.target);
      });
    },
    { threshold: 0.4 },
  );
  headings.forEach((heading) => io.observe(heading));
}

// --------------------------------------------------------------------------
// Portal travel — clicking the hero gate plays a short expanding-circle
// swirl centred on the gate, then navigates. A plain link underneath, so a
// middle-click, a modifier-click, or a browser with JS disabled just
// navigates immediately. Skipped under reduced motion (instant navigation).
// --------------------------------------------------------------------------
function initPortalTravel() {
  const link = document.querySelector('.mf-hero__portal-link');
  if (!link || reducedMotion()) return;

  link.addEventListener('click', (event) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const rect = link.getBoundingClientRect();
    const overlay = document.createElement('div');
    overlay.className = 'mf-portal-transition';
    overlay.style.setProperty('--mf-portal-x', `${rect.left + rect.width / 2}px`);
    overlay.style.setProperty('--mf-portal-y', `${rect.top + rect.height / 2}px`);
    document.body.appendChild(overlay);
    const go = () => { window.location.href = link.href; };
    overlay.addEventListener('animationend', go, { once: true });
    setTimeout(go, 600);
  });
}

// --------------------------------------------------------------------------
// Hidden portal — typing "manifold" anywhere, or the Konami code, opens a
// small overlay with a secret line. Ignored while typing in a form field.
// --------------------------------------------------------------------------
function initHiddenPortal() {
  const KONAMI = ['ArrowUp', 'ArrowUp', 'ArrowDown', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowLeft', 'ArrowRight', 'b', 'a'];
  const WORD = 'manifold';
  let konamiPos = 0;
  let typed = '';

  function openPortal() {
    if (document.getElementById('mf-secret-portal')) return;
    const overlay = document.createElement('div');
    overlay.id = 'mf-secret-portal';
    overlay.className = 'mf-secret-portal';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'A hidden portal');
    overlay.innerHTML =
      '<div class="mf-secret-portal__panel">' +
      '<span class="mf-portal-glow mf-secret-portal__glow" aria-hidden="true"></span>' +
      '<p class="mf-secret-portal__line">Every chart you have opened is still stitched into the atlas somewhere.</p>' +
      '<button type="button" class="mf-btn mf-btn--ghost">Close</button>' +
      '</div>';
    document.body.appendChild(overlay);

    const closeBtn = overlay.querySelector('button');
    function close() {
      overlay.remove();
      document.removeEventListener('keydown', onKey);
    }
    function onKey(event) {
      if (event.key === 'Escape') close();
    }
    closeBtn.addEventListener('click', close);
    overlay.addEventListener('click', (event) => { if (event.target === overlay) close(); });
    document.addEventListener('keydown', onKey);
    closeBtn.focus();
  }

  document.addEventListener('keydown', (event) => {
    const target = event.target;
    if (target && /^(input|textarea|select)$/i.test(target.tagName)) return;

    konamiPos = event.key === KONAMI[konamiPos] ? konamiPos + 1 : (event.key === KONAMI[0] ? 1 : 0);
    if (konamiPos === KONAMI.length) {
      konamiPos = 0;
      openPortal();
      return;
    }

    if (event.key.length === 1) {
      typed = (typed + event.key.toLowerCase()).slice(-WORD.length);
      if (typed === WORD) {
        typed = '';
        openPortal();
      }
    }
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    initTilt();
    initThemeSwirl();
    initHeroParallax();
    initHeroSparks();
    initBuildHeadings();
    initPortalTravel();
    initHiddenPortal();
  });
} else {
  initTilt();
  initThemeSwirl();
  initHeroParallax();
  initHeroSparks();
  initBuildHeadings();
  initPortalTravel();
  initHiddenPortal();
}
