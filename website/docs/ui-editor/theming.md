---
sidebar_position: 8
---

# Theming

The UI editor supports light and dark themes. A sun/moon toggle lives in the page header
next to the Reload button.

#### Light:

![Theme toggle (light)](/img/screenshots/01-entries-list.png)

#### Dark:

![Theme toggle (dark)](/img/screenshots/01-entries-list-dark.png)

## How the toggle works

Click the icon to switch themes. The choice is persisted to `localStorage` under the key
`db-config-theme` and survives page reloads.

The default is **light**. The toggle is binary — there is no "system preference" mode; the
choice is explicit per browser.

## Implementation notes

- Tailwind's class-based dark mode (`@custom-variant dark (&:where(.dark, .dark *))`)
- Theme state lives in a Zustand store at `ui/src/store/themeStore.ts`
- `initTheme()` runs before `createRoot` in `main.tsx` to apply the persisted class to
  `<html>` so the page never flashes the wrong theme on load
- All components use semantic Tailwind tokens (`bg-background`, `text-foreground`,
  `bg-card`, `border-border`, etc.) backed by CSS variables that flip on the `.dark` class

## Docs site theme

The Docusaurus documentation site has its own theme toggle (top-right of the navbar). It
also offers only light/dark — the "system preference" option is disabled
(`respectPrefersColorScheme: false` in `docusaurus.config.ts`).
