# CLAUDE.md

Guidance for coding agents in this repository. People start with [README.md](README.md).

## Project

GameWarden runs Steam dedicated game servers on one Windows machine for a group of friends on a VPN. C# / .NET 10: Core (Windows service, REST API), one Runner per game server, the `gamewarden` CLI and Setup in `src/`; the React web panel in `panel/`. The specs are in `docs/` (read order in the README), the decisions in `decisions/` (ADRs), the plan in [ROADMAP.md](ROADMAP.md).

## Rules

- **Never commit secrets** (passwords, webhook URLs, tokens, signing keys). The repository is public.
- **Nothing that changes the machine runs on the development machine**: no `Setup.exe`, no service install, no user or firewall changes, no admin-only tests. Those are tested by hand on the VM ([ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md), [10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm)).
- **Follow the published pattern** of a library or framework (its official template, docs, examples) instead of inventing one. When a deviation is needed, write down why: in [docs/11](docs/11-web-panel.md) for the panel, in an ADR for architecture.
- **Decisions**: a new decision is a new ADR ([template](decisions/0000-template.md)); accepted ADRs are superseded, not rewritten.
- **Facts vs assumptions**: tag what is not verified **[Unverified]**; dates are absolute (`YYYY-MM-DD`). Docs, comments and commit messages are in English.
- **Git**: work on a branch and open a pull request; never push to `main`, never force-push.

## Commands

```bash
dotnet build GameWarden.slnx                  # also writes panel/openapi/gamewarden-api.json
dotnet test --solution GameWarden.slnx        # Unit, Api, Integration; Windows tests skip themselves off Windows
node build/check-docs.mjs                     # Markdown links and Excalidraw diagrams

# A development Core (~/.local/share/GameWardenDev) and the CLI against it, which finds its token by default.
# The dev box is shared: pick another port with GameWarden__PanelPort=<port> and GAMEWARDEN_URL=http://127.0.0.1:<port>.
dotnet run --project src/GameWarden.Core --launch-profile "Core (development)"
dotnet run --project src/GameWarden.Cli -- status
```

On Linux, Core runs on the development platform of [ADR-0016](decisions/0016-windows-as-an-integration-layer.md); Windows adapters are checked by CI's Windows jobs. The panel's commands are in [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md).

## Panel

Before changing `panel/`, read the working rules ([docs/11 §10](docs/11-web-panel.md#10-working-rules)) and the patterns in [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md). In short: Mantine first; server data only through the generated client and TanStack Query; strict types, generated API types never edited by hand; every string in `en.ts` and `vi.ts`; screenshots in pull requests. After an API change in Core, rebuild Core, run `npm run gen:api` and commit both.
