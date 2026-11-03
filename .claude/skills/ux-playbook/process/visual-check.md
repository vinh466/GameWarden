# Visual check

You cannot judge a layout from code. Look at it. This page says what to capture and what to look for.

## Capture

Use the product's own way to take screenshots first: its scripts, its mock or dev server, its PR rules. If it has
none, use Playwright against the dev server, preferably with mocked data.

| Dimension | Values |
| --- | --- |
| Width | Desktop **1280 px** and phone **390 px**, unless the product sets others |
| Scheme | **Light** and **dark** (Playwright `colorScheme`, or the product's toggle) |
| State | **Populated**, **empty**, **loading**, **error**; for a form, also **invalid** after submit |

Capture the full page for layout, and the element for detail (a dialog, a table row). A minimal Playwright sketch:

```ts
import { chromium } from 'playwright';

const browser = await chromium.launch();
for (const scheme of ['light', 'dark'] as const) {
  for (const width of [1280, 390]) {
    const page = await browser.newPage({ viewport: { width, height: 900 }, colorScheme: scheme });
    await page.goto('http://localhost:5173/servers');
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: `shots/servers-${width}-${scheme}.png`, fullPage: true });
    await page.close();
  }
}
await browser.close();
```

To capture the loading state, delay the mocked response. To capture empty and error, return empty data or an error
from the mock.

## Look

Open each image and answer these. If you cannot view images, say so in the UX notes and list the checks you could
not do. Do not claim a visual result you did not see.

1. **Where does the eye go first?** It should land on the title and the main action.
2. **Is there one primary button?** More than one filled button competing is a fail.
3. **Alignment**: left edges line up, and the actions line up with each other.
4. **Spacing rhythm**: gaps between groups are bigger than gaps inside a group. There are no odd one-off gaps.
5. **Overflow**: no horizontal scroll on the page at 390 px. Long names truncate or wrap on purpose.
6. **Phone layout**: actions folded into a menu or stacked, tables scroll inside their container or become cards,
   and touch targets are at least 24 × 24 px (WCAG 2.2). Aim for about 40 px on screens used on phones.
7. **Dark scheme**: every surface, border, badge and chart reads well. Nothing is pure-white on dark.
8. **States**: the skeleton has the shape of the content, the empty state has one clear action, and the error says
   what happened and offers a retry.
9. **Focus**: tab through the page once (Playwright `keyboard.press('Tab')`). The focus ring is visible on every stop.

## Report

Attach or link the screenshots in the PR. List which state × width × scheme combinations you checked, and what you
changed after looking. A visual fix found here is normal. Record it in the UX notes; don't hide it.
