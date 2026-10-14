# Switchable themes: Retro (default) and Classic

The owner added a second Figma design (`docs/figma/Kutax2`) and asked for it to become the main look, with the original (`docs/figma/Kutax1`) kept as a secondary option.

Both designs use the same three screens and component structure, so Kutax2 is a theme layer, not a second UI:

- **Base.** `frontend/src/styles.scss` holds the Classic design system: tokens and shared classes.
- **Retro layer.** `frontend/src/themes/retro.scss` overrides the tokens: teal and navy, cream surfaces, yellow and orange controls, flat hard shadows and smaller radii. It restyles shared components and a few component internals: the upload board and sign-in art as a dark "screen", the receipt machine, review panes, segmented controls and summary cards.
- **Numbered captions.** The review station's "01 / Queue · 02 / Original · 03 / Extracted" captions (`.panel-cap`) render only in Retro.
- **Scoping.** Selectors are anchored on `html.theme-retro[data-theme='retro']`. Angular's emulated encapsulation adds an attribute to every element in a component selector, so a plain `:root[data-theme]` prefix only ties with component rules, and the component stylesheet loads later. The CSS minifier also dropped `:root` from some selectors. The class-plus-attribute anchor is one notch more specific without `!important`.
- **Switching.** `core/theme.ts` stores the choice in `localStorage` (a per-device convenience; private mode falls back to Retro). It applies the choice in `main.ts` before bootstrap, so there is no flash of the wrong theme, and updates `theme-color`. People switch in Settings → Appearance or from the account menu.

Departures from the Kutax2 export for accessibility:
- Labels are at least 12px; the design used 7–9px.
- The tab and segmented teal is darkened from `#0b7893` / `#0f879b` to `#096a83` (4.48:1 → 5.4:1).
- `--ink-soft` is darkened to `#234357`.
- The queue panel is lightened from `#139eaa` to `#7fd6d3`; no dark text reaches AA on the original.
- Figma placeholder figures (68% readiness, $3,842, "encrypted and stored in Australia", HEIC) are still not shown.

Verified: 60 screen/size combinations per theme pass with no horizontal overflow and zero axe WCAG 2.1 AA violations.
