# Release notes

One file per release, `docs/release-notes/<version>.md`, in the shape of the note posted as
a comment on the ModDB page. The release workflow refuses to publish a tag that has no note,
and puts the note at the top of the GitHub release above the provenance table.

The same text goes to ModDB by hand, as a comment on the mod page, when the zip is uploaded.

## The shape

```
X.Y.Z is released! <one line on what the release is about>

For players

* <what changed on the settings screen, or what was fixed, as a player meets it>

For mod authors

* <a new attribute honoured, a shape that now renders, an API>

Thanks to <Name> (<ModDB profile URL>) for <what they did>.
```

Rules that keep the notes readable side by side:

- **Players first, then mod authors.** Fixes go in whichever section the reader who met
  the problem sits in. Leave a section out only when it would be empty.
- **Plain text.** Headers are a bare line, not Markdown `###`; bullets are `* `; links are
  the URL in brackets. ModDB's comment box is where this is pasted, and every bit of markup
  is a bit to fix there.
- **One bullet per change, one or two short sentences.** Say what the reader sees, not what
  the code does. Name an attribute or member where the reader would type it (`[Range]`,
  `float?`), and nothing internal.
- **No em-dashes.** A second sentence, or a colon, does the job.
- **A fix says what was wrong,** in the terms it was met: the log line, the screen, the file.
- **Credit reporters** at the end, with their ModDB profile URL, and say what they did.
