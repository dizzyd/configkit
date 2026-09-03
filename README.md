# ConfigKit

In-game settings for Vintage Story mods, without Dear ImGui.

ConfigKit reads a mod's `configlib-patches.json`, writes a plain `ModConfig/<mod>.yaml`
you can edit by hand, syncs the server's values to every client, and draws a settings
screen using the game's own interface. Content mods need no changes at all.

> **Status: 0.1.0.** Early, but tested — 39 in-game tests across singleplayer,
> two-process multiplayer, and a run with another config mod installed.

---

## For players

Drop it in your `Mods` folder. Press **P**, or use the **Mod settings** button in the
pause menu.

- Settings the server controls are shown read-only unless you have `controlserver`.
- Your settings live in `ModConfig/`. Edit a file while the game is running and it
  reloads on the spot.
- **Don't install ConfigKit and configlib together.** If ConfigKit sees configlib or
  autoconfiglib it stands down and says so in the log, so nothing breaks — but only one
  of them manages your configs.
- On a server, both sides need it: a client without ConfigKit simply gets no settings
  screen.

## For mod authors

Most mods need **no changes**. Find your case in
**[docs/MIGRATING.md](docs/MIGRATING.md)** — it's four short sections:

| If your mod… | You need to… |
|---|---|
| ships a `configlib-patches.json` and no C# | **nothing** |
| checks `GetMod("configlib")` | change one line |
| calls into the library | swap a reference, rebuild |
| draws its own ImGui settings screen | describe the settings instead — usually *less* code |

A settings class needs only stock .NET attributes — `[Description]`, `[Range]`,
`[Category]` — so it keeps no reference to ConfigKit and still compiles and runs
without it.

---

## Verifying a build

The point of this project is that you shouldn't have to take anyone's word for what a
mod does — including mine.

Every release is built by GitHub Actions and carries Sigstore-signed provenance tying
the zip to the commit and workflow that produced it:

```bash
gh attestation verify configkit_0.1.0.zip --repo dizzyd/configkit
```

The build itself refuses to package anything questionable. `CakeBuild/Program.cs`
asserts that:

1. **`ConfigKit.dll` declares only `ConfigKit.*` and `SimpleExpressionEngine.*` types.**
   Dependencies ship as their own files and are never merged in, so a foreign type
   inside our assembly fails the build. (Negative-tested against a canary.)
2. **Every third-party dll matches its publisher's SHA-256.** `YamlDotNet.dll` is the
   unmodified NuGet 13.7.1 build, resolved at build time rather than checked in.
3. **Every third-party dll ships with its licence**, in `licenses/`.

Be clear about what that does and doesn't prove: it establishes *provenance* — that the
binary contains what the source says and nothing merged in from elsewhere. It is not an
audit of behaviour. Read the source; it's short.

---

## Origin, and credit

ConfigKit is derived from **[configlib](https://github.com/maltiez2/vsmod_configlib)** by
**Maltiez**, released under CC0. The config model, the patch format, the expression
syntax and the server-sync design are all his work, and they are good work — configlib
solved a real problem for hundreds of mods and its file format is the reason ConfigKit
can read existing mods without asking anyone to change anything. Each derived file says
so in its header.

What was rebuilt, and the bugs fixed along the way — including which of them are still
present upstream — is listed in
**[docs/CHANGES-FROM-CONFIGLIB.md](docs/CHANGES-FROM-CONFIGLIB.md)**.

This fork exists because, for a period, configlib's published builds contained a class
that appeared in no commit of its public source, which fingerprinted other loaded
assemblies and degraded or disconnected multiplayer clients after a randomised delay.
Those releases have since been withdrawn, and current configlib builds match their
public source — I checked, and say so gladly.

Nothing here is a judgement of the person. But the principle matters more than any one
incident: **players run our code on their machines, and they should be able to see what
it does.** A mod should do what its source says it does, and nothing else. That's the
whole reason for the verification above — not because this project is more trustworthy,
but because trust shouldn't be the mechanism.

If you use configlib today and it works for you, that's a perfectly reasonable choice.

---

## Building and testing

```bash
export VINTAGE_STORY="$HOME/.cairn/games/1.22.7.app"
./build.sh                      # -> Releases/configkit_<version>.zip
```

Tests run in a real game via [vstestkit](https://github.com/dizzyd/vstestkit):

```bash
run.sh <tests> --mod configkit/configkit --mods tests/fixtures/Mods --client        # singleplayer
run.sh <tests> --mod configkit/configkit --mods tests/fixtures/Mods --multiplayer   # two processes
```

## Licence

MIT — see [LICENSE](LICENSE). Third-party notices are in
[`configkit/licenses/`](configkit/licenses/): YamlDotNet (MIT), SimpleExpressionEngine
(CC0), and configlib (CC0), whose public-domain dedication is what makes this fork
possible.
