# ADR-0022: Settings reach config files through renderers in Core; a server's HTTP API checks it and stops it

- **Status:** Accepted (the Owner approved it on 2026-10-31). Not built yet: the Palworld work waits in the backlog until the Owner starts it ([14 §8](../docs/14-palworld.md#8-in-the-backlog-until-the-owner-starts-it)).
- **Date:** 2026-10-31

## Context

- Palworld comes next, to harden the adapter model for more games ([ADR-0019](0019-discord-joins-the-backlog-mods-next.md)). Its design is [14](../docs/14-palworld.md).
- Today a setting reaches a game only through launch arguments: the `arg` target ([04 §5.2](../docs/04-adapter-spec.md#52-targets)). The spec lists `ini`, `json`, `keyvalue`, `env` and `hook` targets as future work. [ADR-0007](0007-declarative-adapters-with-hooks.md) and 04 §10 planned a PowerShell `renderConfig` hook for Palworld.
- Spike S-P1 showed on 2026-10-31 how Palworld 1.0.5's server behaves ([spike](../spikes/s-p1-palworld/README.md)):
  - All 122 settings sit in **one** line, `OptionSettings=(...)`, of `PalWorldSettings.ini`. The values are booleans, floats, integers, bare enum words, quoted strings, parenthesised lists and empty values.
  - The server reads the file at start and writes it back with the same values.
  - The server has an HTTP REST API with basic auth. It answered 7 s after the start.
  - `POST /save` and then `POST /shutdown` stopped the server cleanly in 2.5 s. Nothing documents that Ctrl+C saves.
- The Owner's answers (2026-10-31, [14 §2](../docs/14-palworld.md#2-the-owners-answers-2026-10-31)): vanilla Palworld; every setting in the panel's form; run it next to Valheim; Steam only.

## Decision

1. **Config files are rendered by Core, from a declaration in the adapter.**
   - The adapter declares a file: its path, the game's template, a format, and fixed fields.
   - A setting targets one field of that file.
   - Before every start, Core renders each declared file as `gamewarden-run` ([ADR-0009](0009-data-root-acl-and-secrets.md)) and keeps a copy in `{paths.config}`. The rendered file is:
     1. the template's fields;
     2. then the fixed fields;
     3. then every setting's value: the stored one, else the schema default.
   - A field that no setting names keeps the template's value. So a field added by a newer game version still gets the game's default.
   - The first format is `ue-struct-ini`: an Unreal `Key=(A=1,B="x",...)` line in an INI section. A plain `ini` format (one key per line) comes when a game needs it; `json` and `keyvalue` come later.
   - The PowerShell hook of ADR-0007 stays for what no format covers. Palworld does not need it.
2. **Two new setting types:**
   - `number`: a decimal, with `min`, `max` and `step`.
   - `list`: a multi-select of `options`, rendered as `(A,B)`.
   - Strings that go into a struct line may not contain `"` or line breaks.
3. **HTTP checks and stops, made by Core to `127.0.0.1`.**
   - A readiness type `http`: a URL, basic auth from a secret, and the expected status.
   - A stop method `http`: a list of requests (Palworld: save, then shutdown with a JSON body). Core then waits for the process to exit up to `timeoutSec`, and kills it after that, as for the other methods.
4. **Internal ports.** A port marked `internal` is allocated and checked for clashes like any other, but gets no firewall rule. Only Core uses it, on loopback. Palworld's REST port is one.
5. **Generated secrets.** A secret marked `generate` gets a random value when an instance is created without one. Palworld's `AdminPassword` is one: Core needs it for the REST API, and the Owner may replace it with one of their own.

## Consequences

- No PowerShell and no hook runner are needed for Palworld. The renderer is plain C#, tested on Linux (ADR-0016), and reusable for the next Unreal game.
- A hand edit of a rendered file is overwritten at the next start for every field a setting or a fixed value names. With every setting in the form, that is all of Palworld's fields.
- A schema default that differs from the template overrides the game's default. Tests compare the schema with the template of the spike's version and allow only listed differences, for example `CrossplayPlatforms` = `(Steam)`.
- These docs change:
  - 04: §3 adds `files`, the `http` readiness and stop, `internal` ports and `generate`; §5 adds the `number` and `list` types and the `file` target, replacing the `ini` / `hook` "M5" rows.
  - 02: stop methods.
  - 05: the settings fields.
  - 06: secrets.
  - 08 §7: the firewall.
- The REST API's admin password sits in plain text in the game's own INI file, which is the game's design. It stays readable by `gamewarden-run` only, as OQ-14 accepted for game passwords.

## Alternatives considered

- **A PowerShell `renderConfig` hook** (ADR-0007, 04 §10): a hook runner, PowerShell under `gamewarden-run`, and no tests on the Linux dev box, for a format C# can write in a few dozen lines.
- **Render from the live file instead of the template:** keeps hand edits. But a setting set back to its default would keep its old value, and a broken file would stay broken.
- **One `string` setting holding the whole `OptionSettings` line:** no form, which is not what the Owner chose.
- **Stop with Ctrl+C:** nothing documents that it saves. REST save and shutdown is documented and tested.
- **RCON:** needs a client of its own. The REST API is the newer, documented interface.
