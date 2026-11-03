# Template: research note

For tier L work: a new flow, a feature area or a navigation change. Write it before building. How to research is
in [research](../process/research.md). Aim for one page.

```markdown
## Research note: <the design question>

**Question**: <one line, phrased as the user's problem, e.g. "How does the owner find and fix a server that will not start?">

**Facts** (from the spec and the product):
- <fact, with a link>

**Missing** (needs the owner; do not invent):
- <business rule or information that is not specified>

**Sources**:
| Source | What it does | Fits our case because / differs because |
| --- | --- | --- |
| <product's own screen X> | … | … |
| <playbook page> | … | … |
| <Primer page / design system / product> | … | … |

**Options**
| | A (safe, built by default) | B | C (optional) |
| --- | --- | --- | --- |
| Idea | … | … | … |
| User gains | … | … | … |
| User costs (steps, attention) | … | … | … |
| Team costs (components, API, effort S/M/L) | … | … | … |
| Business rules touched | none | … | … |
| Risks | … | … | … |

**Recommendation**: <A, B or C, and why in two lines>
**Decision needed**: <the question for the owner>
```

Option A must be buildable today with the playbook's patterns and the product's components. Options B and C may be
creative. They become UX proposals ([format](../process/ux-challenge.md)) until the owner accepts one.
