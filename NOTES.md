# OneNote Markdown Exporter

## 🚢 Releases

GitHub releases are published with the `Release` workflow in Actions.

- Run the workflow manually when you want to publish a release.
- The workflow calculates the version from `version.json` using Nerdbank.GitVersioning.
- It creates the Git tag for the current commit automatically, so you do not need to create tags first.
- It publishes self-contained executables for `win-x64` and `linux-x64` and attaches them to the GitHub release.
- GitHub still provides the usual source code zip and tarball automatically.

### Versioning

- `version.json` currently uses `"version": "1.0"`.
- With Nerdbank.GitVersioning, that becomes release versions such as `1.0.42`, where the last number comes from git height.
- When you want to start a new line, update `version.json` to the next base version such as `1.1` and commit it.
