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

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    initTilt();
    initThemeSwirl();
  });
} else {
  initTilt();
  initThemeSwirl();
}
