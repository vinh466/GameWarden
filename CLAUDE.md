# CLAUDE.md

Guidance for coding agents in this repository. People start with [README.md](README.md).

## Project

GameWarden runs Steam dedicated game servers on one Windows machine for a group of friends on a VPN. C# / .NET 10: Core (Windows service, REST API), one Runner per game server, the `gamewarden` CLI and Setup in `src/`; the React web panel in `panel/`. The specs are in `docs/` (read order in the README), the decisions in `decisions/` (ADRs), the plan in [ROADMAP.md](ROADMAP.md).

## Working mode

On the Owner's requests, agents have full autonomy to develop, debug, merge their own pull requests when CI is green, and release; the Owner tests on the VM. Security trade-offs are accepted for now (private VPN, backed-up data). What must stay right is the product's direction and the docs that later sessions read as the spec:

- **Who decided.** When a choice changes direction (a flow, a default, what a feature does), write down whose choice it was: in the ADR Status, "the Owner asked for it on YYYY-MM-DD" only when the Owner said so, otherwise "decided by the agent". The Owner can then reverse agent decisions later.
- **Docs match the code.** A doc, ADR or ROADMAP item says "built" only for behaviour the code has, with the PR number. Designed but unbuilt parts go in a **Not built yet** list at the top of the doc ([18](docs/18-discord-notifications.md) has one).

## Rules

- **Never commit secrets** (passwords, webhook URLs, tokens, signing keys). The repository is public.
- **Nothing that changes the machine runs on the development machine**: no `Setup.exe`, no service install, no user or firewall changes, no admin-only tests. Those are tested by hand on the VM ([ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md), [10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm)).
- **Follow the published pattern** of a library or framework (its official template, docs, examples) instead of inventing one. When a deviation is needed, write down why: in [docs/11](docs/11-web-panel.md) for the panel, in an ADR for architecture.
- **Decisions**: a new decision is a new ADR ([template](decisions/0000-template.md)); accepted ADRs are superseded, not rewritten. Take the next number from a fresh `origin/main` (`git fetch`, then list `decisions/` on `origin/main`): parallel sessions pick numbers too, and `node build/check-docs.mjs` fails on a duplicate.
- **Facts vs assumptions**: tag what is not verified **[Unverified]**; dates are absolute (`YYYY-MM-DD`). Read today's date with `date +%F` before writing one; never write a date that has not come yet. Docs, comments and commit messages are in English.
- **Git**: work on a branch and open a pull request, docs-only changes too; never push to `main`, never force-push.

## Commands

```bash
dotnet build GameWarden.slnx                  # also writes panel/openapi/gamewarden-api.json
dotnet test --solution GameWarden.slnx        # Unit, Api, Integration; Windows tests skip themselves off Windows
node build/check-docs.mjs                     # Markdown links, Excalidraw diagrams, unique ADR numbers

# A development Core (~/.local/share/GameWardenDev) and the CLI against it, which finds its token by default.
# The dev box is shared: pick another port with GameWarden__PanelPort=<port> and GAMEWARDEN_URL=http://127.0.0.1:<port>.
dotnet run --project src/GameWarden.Core --launch-profile "Core (development)"
dotnet run --project src/GameWarden.Cli -- status
```

On Linux, Core runs on the development platform of [ADR-0016](decisions/0016-windows-as-an-integration-layer.md); Windows adapters are checked by CI's Windows jobs. The panel's commands are in [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md).

## Panel

Before changing `panel/`, read the working rules ([docs/11 §10](docs/11-web-panel.md#10-working-rules)) and the patterns in [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md). In short: Mantine first; server data only through the generated client and TanStack Query; strict types, generated API types never edited by hand; every string in `en.ts` and `vi.ts`; screenshots in pull requests. After an API change in Core, rebuild Core, run `npm run gen:api` and commit both.

For UI and UX decisions, load the **`ux-playbook`** skill: a pinned copy in [.claude/skills/ux-playbook](.claude/skills/ux-playbook/SKILL.md) of [vinh466/ux-playbook](https://github.com/vinh466/ux-playbook) ([ADR-0029](decisions/0029-panel-ux-from-the-ux-playbook-skill.md)). It sizes panel work (S/M/L), picks the Mantine component for a situation, lists the states a screen needs, and says how to challenge a spec, flow or component: build the safe default, then add a **UX proposal** to the pull request for the Owner. GameWarden's own UX rules ([07](docs/07-ux-and-user-flows.md), [11 §3–§6 and §10](docs/11-web-panel.md#3-layout)) win where they differ. `npm run shots` (with `npm run dev:mock` running) takes the pull-request screenshots.
