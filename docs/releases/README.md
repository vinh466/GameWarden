# Development releases

Every release on this line is a GitHub **pre-release** tagged `vX.Y.Z-dev.N`. The panel and `gamewarden update`
always look at the newest release of any kind; there is no channel switch.

Before tagging, copy `TEMPLATE.md` to `<version>.md` (for example `docs/releases/0.3.0-dev.1.md`) and fill in the
data impact. The release workflow uses that file as the GitHub release body. The panel shows the body before the
update confirmation, so it is the one place the Owner sees what to back up and what to re-import.

If a version has no notes file, the workflow publishes a safe default that says to back up
`C:\GameWarden\instances` and `C:\GameWarden\db`. It does not claim that data is kept.

Tagging:

```bash
./build/release-dev.sh              # next v<VersionPrefix>-dev.N
./build/release-dev.sh 0.3.0        # next v0.3.0-dev.N
```

The script checks that the tree is clean, that `main` is up to date, and that the tag does not exist. The tag push
starts `release.yml`, which runs the CI gate, packages, signs `SHA256SUMS`, and creates the pre-release.
