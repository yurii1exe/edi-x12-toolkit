# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions stay
on `0.x` until the typed 214 model has been exercised against a real interchange from
someone other than the author — `1.0.0` is a promise about API stability, and it will not be
made early.

## [Unreleased]

Next up is v0.2, the typed 214 model — see the roadmap in the README.

## [0.1.0-alpha] — 2026-08-15

First release: the envelope, parsed correctly, shipped both as a library and as a tool.
Neither package is on nuget.org yet — this entry describes what `dotnet pack` produces.

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
