# ADR-0029: Panel UX decisions follow a pinned copy of the ux-playbook skill (pilot)

- **Status:** Accepted (the Owner approved the direction and the pilot on 2026-11-03).
- **Date:** 2026-11-03

## Context

- Coding agents build most of the panel: OpenCode sessions on the development box and Claude Code sessions. They use
  Mantine reliably. Layout and hierarchy, missing states, discoverability and flows still need the Owner's attention
  in review.
- [docs/11](../docs/11-web-panel.md) §5–§6 and §10 and [07](../docs/07-ux-and-user-flows.md) set GameWarden's own
  rules (status colours, interaction rules, phone width, screenshots). Mantine's docs explain how to use each
  component. Neither says **when** to choose which component, **which states** a new screen needs, or **how** an
  agent should check its own result.
- On 2026-11-03 the Owner asked for agent-readable UX knowledge that combines Mantine with the reasoning of GitHub's
  Primer design guidance. It should include the right to research and to challenge a spec or flow without changing
  business rules. That knowledge now lives in the public repository
  [vinh466/ux-playbook](https://github.com/vinh466/ux-playbook) (v0.1.0, MIT). It is packaged as an Agent Skill.
- OpenCode and Claude Code both discover project skills in `.claude/skills/<name>/SKILL.md` and load them on demand,
  so a skill costs context only when a task needs it. OpenCode reads this repository's `CLAUDE.md` because there is
  no `AGENTS.md` ([OpenCode rules](https://opencode.ai/docs/rules/), [OpenCode skills](https://opencode.ai/docs/skills/)).
- The Owner chose the challenge level: an agent **builds the safe default and attaches a proposal**. It stops to ask
  before building only for data loss, an exposed secret, an accessibility blocker, or a contradictory or missing
  business rule.
- The Owner wants GameWarden as the first pilot, so that new agent sessions pick the playbook up on their own.

## Decision

1. **A pinned copy** of the skill lives in `.claude/skills/ux-playbook/`. Its `VERSION` file names the source commit.
   It is updated only with the source repository's `scripts/install.sh <this repo>`, never edited here. Changes go to
   the source repository first.
2. **Precedence:** business requirements, then GameWarden's own UX rules (docs/07, docs/11), then the playbook, then
   Mantine's defaults. A conflict between GameWarden's rules and the playbook is raised as a UX proposal, not
   resolved silently.
3. **Working rule 11** in [docs/11 §10](../docs/11-web-panel.md#10-working-rules): panel work is sized S/M/L; a new
   screen or dialog starts with a short UX spec; the pull request ends with **UX notes** and any **UX proposal**.
4. **`npm run shots`** ([panel/e2e/shots.mjs](../panel/e2e/shots.mjs)) takes the screenshots that rule 10 already
   requires, from `npm run dev:mock`. With it, an agent can look at its own result before review.
5. **[CLAUDE.md](../CLAUDE.md)** points agents to the skill in its Panel section. Nothing else is loaded
   automatically.

## Consequences

- New agent sessions see the skill in their skill list and load it for panel work. Sessions running on a branch
  created before this change see it after they rebase.
- The repository carries about 30 Markdown files that are maintained elsewhere. `build/check-docs.mjs` skips
  `.claude/`; the source repository's CI checks the copy's structure and links.
- The pilot is judged by the Owner on the next panel pull requests from agents:
  - **UX notes**: present and honest.
  - **States**: every screen has loading, empty, error and disabled-with-reason.
  - **Screenshots**: taken and looked at.
  - **UX proposals**: useful.
  - **Review time**: fewer UX review comments.
  - **[Unverified]**: whether small, fast models follow the tier process without being reminded.
- Some playbook rules are stricter than today's panel. One example: an unavailable action looks inactive
  (`data-disabled`) instead of using `disabled` with a tooltip. Agents apply GameWarden's rule and may propose the
  change; they do not rewrite existing screens on their own.
- To roll back, delete `.claude/skills/ux-playbook/` and revert the lines in CLAUDE.md, docs/11 and
  panel/CONTRIBUTING.md. `npm run shots` can stay.

## Alternatives considered

- **Install the skill globally on the development box** (`~/.claude/skills`). Not versioned with the repository,
  invisible to other machines and to reviewers, and it would affect every project at once. That is a step for after
  the pilot.
- **Load the playbook through OpenCode's `instructions` (a file or remote URL).** It would be read into every session,
  even one that touches no UI. It works for OpenCode only, and a remote URL is not pinned.
- **Write the guidance into docs/11.** It would mix GameWarden's decisions with general knowledge, it could not be
  shared with other products, and agents would have to be told to read it.
- **Use Primer itself (Primer React, `@primer/mcp`).** The panel uses Mantine ([ADR-0015](0015-panel-mantine-and-toolchain.md)).
  Switching UI kits is out of scope; the playbook keeps Primer's reasoning and maps it onto Mantine.
