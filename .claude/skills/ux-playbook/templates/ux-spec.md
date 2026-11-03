# Template: UX spec

For tier M work, and for each screen of a tier L flow. Put it in the plan or the PR description, before the code.
Aim for 10–20 lines. Fill every heading; write "none" when a heading does not apply.

```markdown
## UX spec: <screen or dialog name>

**User and task**: <who uses it, and the one job they come here to do>
**Entry points**: <how the user gets here: nav item, button on X, link in a notification>
**Main action**: <the one primary action> · **Secondary**: <the others, and where they live (surface or menu)>

**Layout** (top to bottom, desktop; then what changes on a phone):
1. <page header: title, status, actions>
2. <section …>
Phone: <what stacks, what folds into a menu, what scrolls>

**States**
| State | What the user sees |
| --- | --- |
| Loading | <skeleton of …> |
| Empty | <title, text, action> |
| Error | <message, retry> |
| <Other: partial, disabled, no permission, offline> | <…> |

**Components**: <Mantine and product components used, with the playbook page that justified each choice>
**Words**: <title, the main button's label, the key messages>
**Business rules applied**: <fact → how the UI shows it; link the spec>
**Assumptions**: <[Assumption] …>
**Open questions**: <for the owner, or "none">
```

## Example

```markdown
## UX spec: Server "Mods" tab

**User and task**: the owner; see which mods a server runs and add or remove one.
**Entry points**: Server detail → "Mods" tab.
**Main action**: Add mod · **Secondary**: Remove (row menu), Update all (page menu)

**Layout**:
1. Tab header: "Mods" + count badge, "Add mod" button on the right
2. Table: name, version, status badge, updated (relative), row menu
Phone: the table becomes a card list; "Add mod" stays visible; "Update all" moves into the menu

**States**
| State | What the user sees |
| --- | --- |
| Loading | Skeleton rows (5) |
| Empty | "No mods yet" · "Mods add content to the game. Add one from the catalog." · [Add mod] |
| Error | Inline alert "Mods could not be loaded" + [Retry] |
| Server running | Add and Remove disabled, tooltip "Stop the server to change mods" |

**Components**: Table (decisions/data-display), Badge for status, Menu for row actions (decisions/actions), EmptyState
**Words**: Remove confirmation "Remove BetterTrading? The server loses its items on next start." [Remove mod]
**Business rules applied**: mods change only while the server is stopped (spec §4.2) → disabled with the reason
**Assumptions**: [Assumption] fewer than 50 mods per server, so no pagination
**Open questions**: none
```
