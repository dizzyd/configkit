# Third-party notices

ConfigKit itself is MIT (see LICENSE at the repo root). It carries code from two
other projects, whose licences travel with the shipped zip:

| component | how it ships | licence |
|---|---|---|
| YamlDotNet 13.7.1 | `YamlDotNet.dll`, unmodified, alongside `ConfigKit.dll` | MIT — `YamlDotNet-LICENSE.txt` |
| SimpleExpressionEngine | vendored as source under `Vendor/`, compiled in | CC0 1.0 — `SimpleExpressionEngine-LICENSE.txt` |
| ConfigLib | the config core is derived from it | CC0 1.0 — public domain, so no notice required and no constraint on our licence; credited in each file header anyway |

MIT requires its copyright and permission notice to accompany every copy of the
software, so `YamlDotNet-LICENSE.txt` is not optional packaging - the build fails
without it.

Note that YamlDotNet is deliberately **not** merged into `ConfigKit.dll`. See the
comment on `VerifyDependenciesTask` in `CakeBuild/Program.cs` for why.
