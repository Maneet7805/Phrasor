# Design: Phrasor project page

If the page and this file disagree, fix whichever is wrong in the same change. Tokens live at the top of `index.html` (`:root` and `.light`). The page is one file with inline CSS and JS, plus `assets/fonts/` and `media/`.

## Direction

- **Product:** a free Windows tray app that rewrites a rough prompt into a clearer one, shown for review before anything is pasted.
- **Character:** calm, exact, checkable.
- **Signature:** a procedural mountain landscape drawn on a fixed canvas (`#bg`). Its sky shifts as you scroll, and sections switch between dark and light with it.
- **Moments:** the hero composer typing a rough draft, then the pinned "how it works" scene: four steps with annotated screenshots.
- **Source:** the owner's own design. Content was checked against the README, `docs/design/rewrite-intent.md` and the code.

## Colour

The page uses the app's own palette (main window, `docs/design/main-window-plan.md`): warm black, cream, orange.

| Token | Value | Role |
| --- | --- | --- |
| `--ink` | `#181615` | Page background, dark sections (the app's dark window) |
| `--text` | `#F6EBDD` | Cream text on dark |
| `--muted` | `#A89C90` | Secondary text on dark |
| `--peach`, `--peach-hi` | `#F58E4C`, `#F7B27A` | Orange accent: headline emphasis, highlights (the app's accent and focus colours) |
| `--glass` | `rgba(33,30,28,.72)` | Panels over the canvas (the app's card `#211E1C`) |
| `--green` | `#85AB8B` | "Added" in the diff, success |
| `.light` | text `#181615`, muted `#5A5149`, accent `#B4531F` | Light sections (the app's light text and accent text) |

- The canvas sky is warm too: near-black browns at night, cream and tan by day. No blue.
- No purple. The app icon (a ribbon "P" on a dark tile) is the small nav mark.
- Orange is the one accent. Red and green appear only in the diff.
- The screenshot of the main window (`media/main-home.webp`) is a real capture of the dark theme, so the page and the app match.

## Typography

- **Inter**, self-hosted: `assets/fonts/inter-latin.woff2` (variable, weight 300 to 800, latin subset, SIL Open Font Licence, text in `assets/fonts/OFL.txt`). No font request leaves the site.
- Fallback `system-ui, sans-serif`. `font-display: swap`.
- Headlines scale with `clamp()`. Body line height 1.55.

## Shape and depth

- Radius `--r: 18px`. Glass panels (`--glass`) over the canvas. 1px hairlines (`--line`).

## Motion

- The canvas redraws on scroll, eased by `requestAnimationFrame`.
- Reveals and counters run once, through IntersectionObserver.
- `prefers-reduced-motion: reduce` turns off CSS animation and transitions and shows the hero headline at once.

## Content rules

- Every claim traces to the README, `rewrite-intent.md` or the code. No version number on the page: it would go stale.
- Screenshots must not show padding such as "ask before drafting" (`rewrite-intent.md` D8). The two review-window shots (`review-improved`, `review-changes`) still do, and need re-capturing with a real model call; the main-window shot has no rewrite in it.
- Numbers: 128 test drafts, 13 held out, each run 3 times (`eval/cases.json`, `eval/README.md`). Update them when the set changes.
- No analytics, cookies, storage or third-party requests. External links go to GitHub only.

## Do not

- Add third-party fonts, scripts or images.
- Set a custom domain (the Pages setting must stay empty).
