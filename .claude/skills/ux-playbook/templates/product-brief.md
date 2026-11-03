# Template: product UX brief

A product's own UX context: who uses it, for what, and the product-specific rules that override this playbook.
It lives **in the product's repository** (for example `docs/ux.md`), not in the playbook. Agents read it before any
UI work.

If the product already has UX documents, do not write a brief. Point to them from the repository's agent guide
(`AGENTS.md` / `CLAUDE.md`). If it has none, draft one from the repository's docs and code. Mark every guess as
**[Assumption]** and propose the draft to the owner. Never invent business rules to fill a heading.

```markdown
# UX brief: <product>

**Users**: <who, how many, how expert, how often they use it, on what devices>
**Top tasks** (most frequent first):
1. <task> — <where in the UI>
2. …

**Character**: <e.g. "calm operations console", "playful family app">; density: <generous / compact / mixed>
**Platforms and widths**: <desktop-first or phone-first; the widths to check>
**Languages**: <languages; where strings live>

**Product rules that override the playbook**:
- <rule, with the reason or a link>

**Shared components** (use these first):
- `<Component>` — <what it decides> — <path>

**Status and colour vocabulary**: <state → colour + icon + word>
**Words**: <terms the product uses; terms to avoid>
**Sensitive data**: <secrets and personal data, and how the UI treats them>
**Screenshots**: <how to run the app with mock data, and the screenshot command>
**Open questions**: <for the owner>
```
