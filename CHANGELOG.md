# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions stay
on `0.x` until the typed 214 model has been exercised against a real interchange from
someone other than the author — `1.0.0` is a promise about API stability, and it will not be
made early.

## [Unreleased]

### Added

- **The X12 playground** (`web/EdiX12.Playground`) — a Blazor WebAssembly page that parses
  a pasted interchange in the browser and shows its delimiters, envelope, diagnostics and
  segments with the elements named. It has a project reference to `EdiX12.Core`, so it runs
  the library rather than a re-implementation of it. No `HttpClient` is registered and the
  one-click samples are embedded from `samples/`, so the page makes no network request once
  it has loaded. `.github/workflows/pages.yml` builds, tests and publishes it to GitHub
  Pages, where it is served at <https://yurii1exe.github.io/edi-x12-toolkit/>.

### Changed

- Below 900px a diagnostic stacks into a single column: the code and its segment position
  on one line, the element under it, then the message across the full width of the card.
  Measured in Chrome at a 390px viewport, the diagnostics table needs 123px of the card's
  356px where it needed 356.27px.
- `EdiX12.Core` and `EdiX12.Cli` now pack a `.snupkg` and carry Source Link metadata, so a
  consumer can step into the library from their own debugger.

Next up is v0.2, the typed 214 model — see the roadmap in the README.

## [0.1.0-alpha] — 2026-08-15

First release: the envelope, parsed correctly, shipped both as a library and as a tool.
Both packages are prereleases, so `--prerelease` is required to install either one.

### Added — `EdiX12.Core`

- `X12Parser.Parse` — reads an interchange into a typed `Interchange` / `FunctionalGroup` /
  `TransactionSet` / `Segment` tree.
- `X12Tokenizer.ReadDelimiters` — reads all four delimiters from the ISA by fixed offset
  rather than assuming `*` and `~`. ISA11 is only treated as a repetition separator when
  ISA12 declares 00501 or later, so 4010 files do not get split on the letter `U`.
- Recovery for senders who mis-pad the fixed-width ISA fields, by counting separators when
  the fixed offsets do not land on plausible delimiters.
- `Interchange.Validate()` — returns diagnostics instead of throwing, with nine stable
  codes: `X12-IEA-MISSING`, `X12-IEA01-COUNT`, `X12-IEA02-CONTROL`, `X12-GE-MISSING`,
  `X12-GE01-COUNT`, `X12-GE02-CONTROL`, `X12-SE-MISSING`, `X12-SE01-COUNT`,
  `X12-SE02-CONTROL`. Each message names the element and the segment position.
- `X12ParseException` for structural failures only — an unreadable ISA, an SE with no ST.
- `Segment.Components` and `Segment.Repetitions` for composite and repeating elements.
- Targets `netstandard2.0` and `net8.0`.

### Added — `EdiX12.Cli`

- **`EdiX12.Cli`** — the parser as a .NET global tool, `edix12`. Three commands:
  - `edix12 parse FILE` writes the envelope, its transaction bodies and its diagnostics as
    one JSON document; `--pretty` indents it.
  - `edix12 validate FILE` writes the diagnostics as an aligned table and **exits 1** when
    there are any, which is what makes it usable as a build step. `--json` for machines.
  - `edix12 delimiters FILE` writes the four delimiters with the ISA offset each was read
    from, and says why there is no repetition separator when there isn't one.
  - Reads stdin when the file argument is `-` or absent, writes to a file with
    `-o/--output`, and colours its output only when stdout is a terminal. `--color` and
    `--no-color` override that, and `NO_COLOR` is honoured.

### Added — samples

- `samples/214-shipment-status.edi` and `samples/214-pipe-delimited.edi`, the same document
  under different delimiters, asserted by test to parse to the same object graph.
- `samples/214-broken.edi`, the same 214 with SE01, GE01 and IEA02 wrong, and
  `samples/214-4010.edi`, a 4010 interchange whose ISA11 is the standards identifier `U`
  rather than a repetition separator. Both are asserted by test.

### Notes

There is no typed 214 model in this release. You get the envelope and a flat segment list.

[Unreleased]: https://github.com/yurii1exe/edi-x12-toolkit/compare/v0.1.0-alpha...HEAD
[0.1.0-alpha]: https://github.com/yurii1exe/edi-x12-toolkit/releases/tag/v0.1.0-alpha
