# Voltura Air TODO

Approved unfinished work is ordered here. Current behavior belongs in
[features](features.md); possible directions belong in [ideas](ideas.md).
Remove completed items after updating their current authority.

## Priority

1. Release-blocking correctness, security, connection, input, data-loss,
   recovery, and resource-lifetime defects.
2. Presentation graduation.
3. Work promoted from `ideas.md` after its outcome, priority, ownership, and
   validation boundary are decided.

## Presentation graduation

- [ ] Add reusable host presentations to the existing Presentations page:
  request a name and PowerPoint file; store an opaque ID; show a persistent
  `Host` row without statistics and prefer it over report-derived candidates
  for the same canonical file. Never delete the PowerPoint file when deleting a
  reusable item or a run; retain the Host row and save each run as an ordinary
  device-attributed report linked to it.
- [ ] Inspect modern Open XML presentations without launching PowerPoint:
  bounded total/visible/hidden slide counts, dimensions/aspect ratio, format,
  modified time, and inspection status. Refresh on add, manual refresh, and
  immediately before launch. Inspect legacy `.ppt` only after PowerPoint opens
  it. Cover malformed/locked files and cleanup.
- [ ] Complete integrated and real-device checks for PowerPoint, Google Slides,
  and PDF/browser presentation control, timing, report saving, custom laser
  restoration, adaptive layouts, and accessibility.
- [ ] Review implemented Presentation behavior against `features.md`, protocol,
  privacy, UI, and host authorities.
- [ ] Remove the alpha gate and alpha-only wording in one reviewed change while
  retaining Presentation permission enforcement.
