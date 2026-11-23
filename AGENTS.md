# AGENTS.md

The canonical working rules for every coding agent in this repository are in [CLAUDE.md](CLAUDE.md). They apply to
Claude, OpenCode and any other agent; this file only points to them so agent tooling that looks for `AGENTS.md`
finds the same source.

- **Rules:** [CLAUDE.md](CLAUDE.md)
- **Documentation map, by task:** [docs/README.md](docs/README.md)
- **Plan and exit criteria:** [ROADMAP.md](ROADMAP.md)
- **Panel patterns:** [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md)

In short: never commit secrets; do not run anything that changes the machine on the development box; work on a
branch and open a pull request; make the docs match the code and record a new direction as an ADR.
