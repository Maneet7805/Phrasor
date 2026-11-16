# Third-party files shipped inside Phrasor

NuGet packages (DiffPlex, NAudio, Microsoft.Web.WebView2 and others) keep their own licences; see the package pages. This file lists the font and image files that are copied into the main window by hand.

## Fonts

Both fonts are licensed under the **SIL Open Font License, Version 1.1**, which allows bundling and redistributing them inside software, provided the copyright notice and licence travel with them and the fonts are not sold on their own. Neither declares a Reserved Font Name. The files are used as published by the Fontsource project (an npm repackaging of the official fonts, Latin subset, WOFF2), unmodified by Phrasor, and are served only from inside the app (nothing is fetched from a font server).

| Font | Files (in `src/Phrasor.App/Web/fonts/`) | Copyright | Source | SHA-256 |
| --- | --- | --- | --- | --- |
| Fraunces (variable: weight and optical size) | `fraunces-latin-opsz-normal.woff2`, `fraunces-latin-opsz-italic.woff2` | Copyright 2020 The Fraunces Project Authors (github.com/undercasetype/Fraunces) | `@fontsource-variable/fraunces` 5.3.0 | normal `7234ed86…d783`, italic `066710ce…8271` |
| Inter (variable: weight) | `inter-latin-wght-normal.woff2` | Copyright 2016 The Inter Project Authors (github.com/rsms/inter) | `@fontsource-variable/inter` 5.3.0 | `3100e775…4c62` |

The full licence text is the one in each package's `LICENSE` file, and is also at https://openfontlicense.org/open-font-license-official-text/.

The Latin subset covers English and most Western European text. Text a user types or sees in the page that needs other scripts falls back to the system font stack.

## Images

`src/Phrasor.App/Web/img/hero.webp` and `logo.png` are Phrasor's own artwork (the project's logo exploration); they are covered by the repository licence.
