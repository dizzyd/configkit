# Regression review: flat configs, September 2026

> All three were fixed in `aeb2af2`, and `tests/CompatibilityTests.cs` now holds this build
> against a recorded baseline of v1.2.0 so the class of fault is caught rather than reviewed
> for. Kept as the record of how they were found.


Reviewed the complex-config changes from `10f69ad` (before nested-config support)
through `f6a7a3a` on September 5, 2026. Three regressions were reproduced with
executable before/after probes.

## 1. [P1] Saved values can revert during JsonProperty migration

- **Introduced in:** `90af388`
- **Location:** `configkit/Config/Config.cs:637`, `ReadStoredValue`

For a flat member declared as:

```csharp
[JsonProperty("speed")]
public int Speed = 1;
```

an existing file containing `{"speed":1,"Speed":8}` loaded **8** before the
changes but loads **1** now.

Earlier ConfigKit could produce both keys by preserving the mod's serialized
`speed` key while saving edits under the C# member name, `Speed`. The new reader
always prefers the serialized name and consults legacy names only when that key
is absent. Consequently, a stale serialized value can take precedence over the
player's newer saved value when upgrading.

Migration needs to account for files containing both names. The existing renamed
member test covers a file containing only the legacy name, so it misses this
conflict.

## 2. [P2] Float sliders lose declared step precision

- **Introduced in:** `377f9bb`
- **Location:** `configkit/Gui/ConfigDialog.cs:1161`, `ScaleFor`

A flat float setting with range `0–100` and an explicit step of `0.01` now uses
tenths. The probe converted the setting value to a slider position and passed
that position through the slider callback:

| Result | Before | After |
| --- | --- | --- |
| Value after slider conversion | `43.27` | `43.3` |
| Slider position | `4327` | `433` |
| Converted `0.01` step | `1` | `0` (clamped to `1` by widget setup) |

`ScaleFor` chooses precision solely from range width, ignoring the declared step.
The resulting slider cannot select values that were previously supported. Scale
selection should preserve explicit step precision.

## 3. [P2] Standalone headings and explanatory text disappear

- **Introduced in:** `6cfe069`
- **Location:** `configkit/Gui/ConfigDialog.cs:545`, `LayoutRows`

The new layout removes titled formatting blocks from the block sequence and
renders their headings only when a subsequent setting triggers them. A trailing
separator therefore disappears, including explanatory text attached to it.
Consecutive headings without intervening settings have the same problem.

The probe compared a flat definition containing one boolean setting with the
same definition followed by:

```json
{
  "type": "separator",
  "title": "Help",
  "text": "Restart after changing"
}
```

| Measured layout height | Before | After |
| --- | --- | --- |
| Setting alone | `42` | `46` |
| Setting plus trailing separator | `110` | `46` |

The trailing heading and text contributed layout before but contribute nothing
now. Flat definition screens should preserve standalone formatting blocks in
their original order.

## Verification and limits

- Built isolated snapshots of `10f69ad` and `f6a7a3a` and ran the same probes
  against both, using the installed Vintage Story 1.22.7 assemblies and a stub API.
- All three cases above reproduced different behavior between the snapshots.
- Basic flat integer, boolean, string, and float loading passed in both versions;
  saving and reloading an edited integer also passed.
- GUI verification exercised layout measurement and slider conversion/callback
  methods without rendering a live game screen.
- The full in-game test suite was not run.
- No implementation changes were made as part of this review.
