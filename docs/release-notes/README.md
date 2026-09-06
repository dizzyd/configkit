# Release notes

One file per release, `docs/release-notes/<version>.md`, in the shape of the note posted as a
comment on the ModDB page. The release workflow refuses to publish a tag that has no note,
and puts the note at the top of the GitHub release above the provenance table.

The same text goes to ModDB by hand, as a comment on the mod page, when the zip is uploaded.

## The shape

```
X.Y.Z is released! <one line on what the release is about, or what it covers>

### For mod authors

*   <a change an author sees: a new attribute honoured, a shape that now renders, an API>

### For players

*   <a change a player sees on the settings screen>

### Fixes

*   <what was wrong, in the terms the player or author would have met it>

Thanks to [Name](https://mods.vintagestory.at/show/user/...) for <what they did>.
```

Rules that keep the notes readable side by side:

- **Three sections, always in that order.** Leave one out only when it would be empty.
- **One bullet per change, one line each,** written as what changed for the reader — "sliders
  show the setting's own value", not "refactored ToSliderInt". Name the attribute or member
  where the reader would type it (`[Range]`, `float?`), and nothing internal.
- **A fix says what was wrong,** in the terms it was met: the log line, the screen, the file.
  The cause can follow a colon if it is short and interesting.
- **Credit reporters** at the end, linked to their ModDB profile, and say what they did.
- ModDB comments render this Markdown: `###` headings, `*   ` bullets, links. Keep to that
  subset.
