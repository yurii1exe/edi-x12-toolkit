# EdiX12

[![CI](https://github.com/yurii1exe/edi-x12-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/yurii1exe/edi-x12-toolkit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Playground](https://img.shields.io/badge/playground-open-58a6ff)](https://yurii1exe.github.io/edi-x12-toolkit/)

Parse and validate ANSI X12 freight EDI in .NET.

### → [Open the playground](https://yurii1exe.github.io/edi-x12-toolkit/)

Paste an interchange and get back the delimiters it declared, its envelope, its diagnostics
and every segment with the elements named — in about as long as it takes to read the
rejection email. Nothing to install, nothing to sign up for, nothing uploaded: the parser
is `EdiX12.Core` compiled to WebAssembly and run by your browser, so the file stays on your
machine.

![The edix12 CLI reading the delimiters out of two 214 files that use different delimiters, validating a broken envelope into three named diagnostics with exit code 1, and emitting the transaction segments as JSON](https://raw.githubusercontent.com/yurii1exe/edi-x12-toolkit/main/docs/edix12-demo.gif)

<sub>The same command against two files. The first is delimited with `*` and `~`, the second
with `|` and a newline — both read out of their own ISA rather than assumed. Then a broken
envelope: three diagnostics that name the element, and exit code 1 so it works as a build
step. The transcript is real — every command was run against the packed `edix12 0.1.0-alpha`
tool and the samples in this repository.</sub>

Freight EDI is a solved problem that everyone re-solves badly. Most integrations start
with `text.Split('~')` and `segment.Split('*')`, work fine against the first partner, and
break the day a partner sends a file delimited with `|` and terminated with a newline —
which is entirely legal, and common.

EdiX12 handles the envelope, the delimiters and the control numbers correctly, so you work
with typed objects instead of string-splitting.

[![EdiX12.Core on nuget.org](https://img.shields.io/nuget/vpre/EdiX12.Core.svg?label=EdiX12.Core)](https://www.nuget.org/packages/EdiX12.Core/)
[![EdiX12.Cli on nuget.org](https://img.shields.io/nuget/vpre/EdiX12.Cli.svg?label=EdiX12.Cli)](https://www.nuget.org/packages/EdiX12.Cli/)

```bash
dotnet add package EdiX12.Core --prerelease
```

The two badges query nuget.org when you load this page and report the version on the feed,
so they — not this file — are what tells you whether that command resolves right now.
`--prerelease` is not optional for `0.1.0-alpha`: NuGet will not resolve a prerelease
without the flag.

Neither package has to resolve for you to run the parser. The playground is one click, and
from a clone `dotnet run --project src/EdiX12.Cli -- validate samples/214-broken.edi`
builds and runs the tool with no package involved. Every package is produced by the CI pack
job, which installs the `edix12` tool from the package it has just built and runs it
against the samples in this repository before the artifact is kept.

```csharp
var interchange = X12Parser.Parse(File.ReadAllText("shipment.edi"));

Console.WriteLine(interchange.SenderId);        // DEMOSENDER
Console.WriteLine(interchange.IsProduction);    // False — ISA15 was 'T'

foreach (var diagnostic in interchange.Validate())
{
    Console.WriteLine(diagnostic);
    // X12-SE01-COUNT at segment 9: SE01 (Number of Included Segments) declares '9',
    // transaction set 214 0001 contains 7 segments counting ST and SE.
}
```

## The delimiter problem

X12 has no fixed delimiters. `*` and `~` are conventions, not rules. Every interchange
declares its own delimiters inside its ISA segment, and ISA is the only fixed-width segment
in X12 — exactly 106 characters — precisely so a reader can find them by offset before it
knows how to split anything:

```
offset 3     the data element separator
offset 104   ISA16, the component element separator
offset 105   the segment terminator
offset 82    ISA11, the repetition separator — but only from 5010 onward
```

Two traps sit in that list.

The first is that the component separator is **element ISA16**, which lives at character
offset **104**. Offset 16 is the tail of ISA02. Both numbers are "16" and only one of them
is right.

The second is ISA11. In 5010 it is the repetition separator. In 4010 it is the Interchange
Control Standards Identifier, conventionally `U`. Read it as a delimiter in a 4010 file and
you start splitting element values on the letter U. EdiX12 checks ISA12 before deciding.

The segment terminator is never named by an element at all — it is simply whatever
character follows ISA16, which is why it has to be read rather than assumed.

```csharp
var delimiters = X12Tokenizer.ReadDelimiters(text);
// element='|' component='>' segment='\n' repetition='!'
```

`samples/214-shipment-status.edi` and `samples/214-pipe-delimited.edi` are the same
document with different delimiters. They parse to the same object graph. There is a test
that asserts it.

## The playground

### → [yurii1exe.github.io/edi-x12-toolkit](https://yurii1exe.github.io/edi-x12-toolkit/)

Paste the interchange a partner just rejected and read what is wrong with it. No install,
no upload, no account.

![The X12 playground loading two of its built-in samples: the delimiter table changing from asterisk, colon and tilde to pipe, greater-than and a newline when the same document is loaded pipe-delimited, the segment-by-segment breakdown, and the broken sample's X12-SE01-COUNT diagnostic with the SE row highlighted](https://raw.githubusercontent.com/yurii1exe/edi-x12-toolkit/main/docs/playground-demo.gif)

<sub>The recording starts on `samples/214-shipment-status.edi`: the delimiters that
interchange declared, where each one was read from in the ISA, the envelope, and nine
passing envelope checks. One click loads the same document delimited with `|` and
terminated by a newline — the table follows the ISA. Then every segment with its envelope
role and its elements by number, and one more click loads the broken sample:
`X12-SE01-COUNT` on `SE01`, which declares 9 segments where the transaction set contains 7,
with that SE row highlighted in the segment table. Every parse in the frame is
`EdiX12.Core` compiled to WebAssembly and executed by the browser — the interchange is
never uploaded, and once the page has loaded no request leaves it. The live page opens on
the broken sample, so those three diagnostics are on screen before you click anything.</sub>

`web/EdiX12.Playground` is a browser page that parses an interchange you paste into it and
shows the delimiters it declared, its envelope, its diagnostics and every segment with the
elements named. It is a Blazor WebAssembly app with a project reference to
`src/EdiX12.Core`, so the parse you see is `X12Parser.Parse` — the same method the package
ships — compiled to WebAssembly and executed by your browser.

That reference is the point. A JavaScript re-implementation would be a second parser with
its own bugs, and a demo that agreed with the library only by coincidence. This one cannot
disagree with it: there is only one parser in the repository.

Nothing is uploaded. `Program.cs` registers no `HttpClient`, and the four one-click samples
are embedded resources built from `samples/` rather than fetched. Neither the page nor its
stylesheet loads a resource from another host — no CDN script, no web font, no analytics;
the only external URLs on the page are links you can choose to click. After the page has
loaded it makes no network requests at all. There is a test that asserts the no-external-
resource half of that.

Run it locally:

```bash
dotnet run --project web/EdiX12.Playground
```

The playground is **not** part of `EdiX12.sln`. It targets `net10.0`, and keeping it out is
what lets the claim below — that the .NET 8 SDK builds and tests everything in the solution
— stay true. It has its own solution:

```bash
dotnet test web/EdiX12.Playground.sln     # 47 tests, .NET 10 SDK
```

Twelve of those render the page component into a DOM and assert what a visitor sees: that
the broken sample's three diagnostics arrive in segment order with `SE01` named beside
`X12-SE01-COUNT`, that the SE, GE and IEA rows are the ones highlighted, that a
pipe-delimited file reports `|`, `>` and `\n`, and that a 4010 interchange says ISA11 is
not a delimiter rather than showing you a `U`.

`dotnet publish` produces a static site — 6.2 MB across 42 files, of which 6.1 MB is the
.NET runtime and the base class library and 23 KB is `EdiX12.Core` itself. GitHub Pages
compresses on the fly, so a first visit transfers about 2.4 MB of that: `dotnet.native.wasm`
is 2.9 MB on disk and 1.1 MB over the wire, `System.Private.CoreLib.wasm` 1.5 MB and 566 KB.
The runtime is cached by the browser after the first visit.

`.github/workflows/pages.yml` builds, tests and publishes it to GitHub Pages on every push
that touches `web/`, `src/EdiX12.Core/` or `samples/`, rewriting `<base href>` to the
project-site path and writing a `.nojekyll` so that `_framework` is not swallowed. What it
deploys is served at **<https://yurii1exe.github.io/edi-x12-toolkit/>**.

## Command line

`EdiX12.Cli` packages the same parser as a .NET global tool called `edix12`:

```bash
dotnet tool install --global EdiX12.Cli --prerelease
```

From a clone, `dotnet run --project src/EdiX12.Cli -- <command>` does the same thing
without installing anything. Every transcript below is real output, copied verbatim.

Three commands. Each takes a file path, or reads the interchange from stdin when given `-`
or nothing at all.

### `edix12 delimiters` — what the ISA actually declares

```console
$ edix12 delimiters samples/214-shipment-status.edi
samples/214-shipment-status.edi   ISA12 00501

  element separator      *   ISA offset 3, the one delimiter readable without the others
  component separator    :   ISA16 - at offset 104, not offset 16
  segment terminator     ~   offset 105, the character after ISA16; no element names it
  repetition separator   ^   ISA11 at offset 82, a delimiter only from 00501 onward
```

Same document, delimited with `|` and terminated with a newline. Nothing about the file
had to be configured — every character below was read out of its ISA:

```console
$ edix12 delimiters samples/214-pipe-delimited.edi
samples/214-pipe-delimited.edi   ISA12 00501

  element separator      |    ISA offset 3, the one delimiter readable without the others
  component separator    >    ISA16 - at offset 104, not offset 16
  segment terminator     \n   offset 105, the character after ISA16; no element names it
  repetition separator   !    ISA11 at offset 82, a delimiter only from 00501 onward
```

The bottom row is the version-dependent one. `samples/214-4010.edi` is a 4010 interchange,
where ISA11 holds the Interchange Control Standards Identifier `U` and is not a delimiter
at all:

```console
$ edix12 delimiters samples/214-4010.edi
samples/214-4010.edi   ISA12 00401

  element separator      *   ISA offset 3, the one delimiter readable without the others
  component separator    :   ISA16 - at offset 104, not offset 16
  segment terminator     ~   offset 105, the character after ISA16; no element names it
  repetition separator   -   none - ISA12 is 00401, where ISA11 is the standards identifier
```

### `edix12 validate` — the envelope, and an exit code

`samples/214-broken.edi` is the 214 above with three numbers wrong: SE01 over-counts, GE01
over-counts, and IEA02 does not echo ISA13. Those are the numbers a partner rejects a file
over, and the rejection almost never names which one:

```console
$ edix12 validate samples/214-broken.edi
samples/214-broken.edi

  X12-SE01-COUNT      segment 9    SE01 (Number of Included Segments) declares '9', transaction set
                                   214 0001 contains 7 segments counting ST and SE.
  X12-GE01-COUNT      segment 10   GE01 (Number of Transaction Sets Included) declares '2', group 1
                                   contains 1.
  X12-IEA02-CONTROL   segment 11   IEA02 (Interchange Control Number) is '000000002' but ISA13 is
                                   '000000001'. The trailer must echo the header control number
                                   exactly.

3 diagnostics

$ echo $?
1
```

A sound envelope says so, and exits 0:

```console
$ edix12 validate samples/214-shipment-status.edi
samples/214-shipment-status.edi

  OK  no diagnostics - the envelope is structurally sound.

Envelope checks only. This says nothing about the business data.
```

Which is the whole point of the exit code — it makes the tool a build step:

```console
$ for f in samples/*.edi; do edix12 validate "$f" > /dev/null || echo "rejected: $f"; done
rejected: samples/214-broken.edi
```

### `edix12 parse` — JSON out

```console
$ edix12 parse samples/214-shipment-status.edi --pretty | head -20
{
  "delimiters": {
    "element": "*",
    "component": ":",
    "segment": "~",
    "repetition": "^"
  },
  "interchange": {
    "senderQualifier": "ZZ",
    "senderId": "DEMOSENDER",
    "receiverQualifier": "ZZ",
    "receiverId": "DEMORECEIVER",
    "date": "2026-08-15T14:30:00",
    "versionNumber": "00501",
    "controlNumber": "000000001",
    "acknowledgmentRequested": false,
    "usageIndicator": "T",
    "isProduction": false,
    "segmentCount": 11,
    "groups": [
```

Without `--pretty` it is one line, which is what you want when something else is reading
it. The transaction body is addressable down to the element:

```console
$ edix12 parse samples/214-shipment-status.edi | jq -c '.interchange.groups[].transactions[].segments[] | {id, elements}'
{"id":"B10","elements":["4938","SHIPMENT001","DEMO"]}
{"id":"LX","elements":["1"]}
{"id":"AT7","elements":["AF","NS","","","20260815","1430","LT"]}
{"id":"MS1","elements":["ATLANTA","GA","US"]}
{"id":"MS2","elements":["DEMO","TRAILER123"]}
```

`parse` carries the diagnostics inside the JSON rather than splitting them across two
streams, and does not fail on them — `validate` is the command that judges a file. `--json`
gives `validate` and `delimiters` the same treatment:

```console
$ edix12 validate samples/214-broken.edi --json | jq -c '.diagnostics[] | {code, segmentPosition}'
{"code":"X12-SE01-COUNT","segmentPosition":9}
{"code":"X12-GE01-COUNT","segmentPosition":10}
{"code":"X12-IEA02-CONTROL","segmentPosition":11}

$ edix12 delimiters samples/214-pipe-delimited.edi --json
{"element":"|","component":">","segment":"\n","repetition":"!"}
```

The JSON carries the delimiter characters themselves, so a newline terminator arrives as
`"\n"` rather than as the word "newline". The human rendering is the only place they are
described.

### Piping, colour, exit codes

Anything on stdin works, with `-` or with no file argument at all:

```console
$ cat samples/214-pipe-delimited.edi | edix12 validate
<stdin>

  OK  no diagnostics - the envelope is structurally sound.

Envelope checks only. This says nothing about the business data.
```

Colour is on when stdout is a terminal and off the moment it is redirected, so what you
pipe into a file is exactly the text printed above. `--color` and `--no-color` override
that, and [`NO_COLOR`](https://no-color.org) is honoured.

| Option | |
|---|---|
| `--pretty` | indent the JSON |
| `--json` | JSON from `validate` and `delimiters` instead of the table |
| `-o`, `--output PATH` | write to a file instead of stdout |
| `--color`, `--no-color` | force colour on or off |

| Exit code | |
|---|---|
| `0` | the command succeeded |
| `1` | `validate` found diagnostics |
| `2` | the interchange could not be parsed at all |
| `3` | bad arguments, or the file could not be read or written |

Code 2 is reserved for files that cannot be read at all, and it keeps the library's
explanation rather than reducing it to "parse error":

```console
$ head -c 40 samples/214-shipment-status.edi | edix12 delimiters
edix12: cannot parse <stdin> at segment 1
  Could not locate ISA16 (the component element separator). Expected it at offset 104 of the ISA
  segment, and found only 6 of the 16 element separators an ISA must contain. The input holds only
  40 characters from the start of the ISA, shorter than the 106 characters of a complete ISA
  segment, so it is most likely truncated.

$ echo $?
2
```

## What it does today

**v0.1 — the envelope.** ISA/GS/ST … SE/GE/IEA parsed into a typed tree, with the
delimiters read from the interchange rather than assumed.

- Reads all four delimiters from the ISA, including the 5010 repetition separator
- Recovers when a sender gets the ISA field widths wrong and the fixed offsets shift
- Line endings between segments are optional; CRLF, LF and single-line files parse identically
- Empty elements keep their position, so element 05 stays element 05
- Element values are preserved exactly as sent; trimming is the typed layer's decision
- Shipped both ways: `EdiX12.Core` as a library, `EdiX12.Cli` as the `edix12` global tool

What it does **not** do yet: there is no typed 214 model. You get the envelope and a flat
list of segments whose elements are addressed by their spec number, not `status.ShipmentId`:

```csharp
Segment b10 = interchange.Transactions.First()
    .Segments.First(s => s.Id == "B10");

Console.WriteLine(b10[2]);   // B1002 — the shipment identification number
```

Looking a segment up by its identifier is your `.First(s => s.Id == …)` for now; there is
no lookup by name on the object model yet. That, and the typed model, is v0.2 — see the
roadmap.

**Validation** covers what a receiving partner checks before it looks at your business
data — the three trailer echoes, the three counts, and the three envelope segments that
have to be there at all:

| Code | Check |
|---|---|
| `X12-IEA-MISSING` | The ISA is closed by an IEA |
| `X12-IEA01-COUNT` | IEA01 group count matches reality |
| `X12-IEA02-CONTROL` | IEA02 echoes ISA13 |
| `X12-GE-MISSING` | The GS is closed by a GE |
| `X12-GE01-COUNT` | GE01 transaction count matches reality |
| `X12-GE02-CONTROL` | GE02 echoes GS06 |
| `X12-SE-MISSING` | The ST is closed by an SE |
| `X12-SE01-COUNT` | SE01 segment count matches reality, counting ST and SE |
| `X12-SE02-CONTROL` | SE02 echoes ST02 |

A wrong SE01 is the most common reason a partner rejects an otherwise correct file, and the
rejection almost never says which number was wrong.

`Validate()` returns diagnostics rather than throwing. A parser that refuses to show you a
bad file is useless for the one job you actually need it for, which is working out why the
partner rejected it. Only structural failures — an unreadable ISA, an SE with no ST —
throw `X12ParseException`.

## Roadmap

```
v0.1  Envelope: ISA/GS/ST parsed into a typed tree      <- you are here
v0.2  214 typed model: shipment, stops, status events
v0.3  Generate a 214 from an object
v0.4  997 functional acknowledgment
v0.5  204 load tender, 210 invoice
```

Scope is deliberately one transaction set at a time. 214 (Transportation Carrier Shipment
Status Message) comes first because it is the highest-volume message in freight — every
status update on every load.

## Building

```bash
dotnet build
dotnet test
dotnet run --project src/EdiX12.Cli -- validate samples/214-broken.edi
```

The .NET 8 SDK is the minimum for `EdiX12.sln`, and it is enough: there is no `global.json`
pinning a newer SDK, the solution is in the classic `.sln` format, and both the test project
and the CLI target `net8.0`. The browser playground is the one thing that needs a newer SDK,
which is why it sits in `web/EdiX12.Playground.sln` and not in this one.
CI runs the whole build on 8.0.x and 10.0.x, on Linux and Windows, restoring from a clean
runner with no `NuGet.config` — so the package needs nothing but nuget.org.

`EdiX12.Core` targets `netstandard2.0` and `net8.0`. netstandard2.0 is there because a
large part of the 3PL world is still on .NET Framework, and an EDI library that they cannot
reference is an EDI library they will not use. Note that the test suite resolves the
`net8.0` asset — the netstandard2.0 build is compiled by CI but is not yet exercised by
tests on .NET Framework.

## Provenance

Everything in this repository is written from the public ANSI X12 specification. Every
fixture and sample is invented: `DEMOSENDER`, `DEMORECEIVER` and the SCAC `DEMO` are not
real trading partners, and no part of this code or its test data derives from any
production interchange or any partner's implementation guide.

This project is not affiliated with, endorsed by, or derived from the Accredited Standards
Committee X12. "X12" is used here only to name the standard the library reads.

## Demo media

Both recordings shown above are held in `docs/`, alongside the three playground captures. The
published videos and their posters are cut from the recordings; the published stills are either
frames from those recordings or captures of the deployed build at
<https://yurii1exe.github.io/edi-x12-toolkit/>.

| Source | What it shows | Published as |
|---|---|---|
| `docs/edix12-demo.gif` | 1336×928, 29s. `edix12 delimiters` against two 214 files that declare different delimiters, `edix12 validate` turning a broken envelope into three named diagnostics and exiting non-zero, and `edix12 parse` piped through `jq` | `cli.mp4`, `cli.webm`, the poster `cli.webp`, and the still `cli-json.webp` |
| `docs/playground-demo.gif` | 880×560, 18s. The browser playground on two built-in samples — the delimiter table following each interchange's own ISA, the segment table with every envelope role, and the broken sample resolving to `X12-SE01-COUNT`, `X12-GE01-COUNT` and `X12-IEA02-CONTROL` | `playground.mp4`, `playground.webm`, and the poster `playground.webp` |
| `docs/live-diagnostics.png` | 2880×1800. A deliberately broken interchange pasted into the live playground, resolving to `X12-SE01-COUNT`, `X12-GE01-COUNT` and `X12-IEA02-CONTROL`, with the failing rows flagged in the segment table below | `live-diagnostics.webp` |
| `docs/live-segments.png` | 2880×1800. The built-in *214 shipment status* sample parsed, every segment listed in order with its elements numbered | `live-segments.webp` |
| `docs/live-delimiters.png` | 2880×1800. The built-in *Same file, pipe-delimited* sample, its delimiter table reporting each character and the ISA position it came from | `live-delimiters.webp` |

The published files live in the site repository under
`TheSite/ClientApp/src/assets/portfolio/edi-x12-toolkit/`, with the card thumbnail
`edi-x12-toolkit.webp` one directory above. Provenance runs two ways and this repository holds the
source for both. The videos and their posters are produced from the two recordings with ffmpeg —
mp4 and webm per recording, a webp poster beside them. The stills are either frames cut from those
recordings or Playwright captures of the deployed playground, converted to webp with ffmpeg. Every
derivative is regenerated rather than edited.

They feed the `edi-x12-toolkit` entry on disit.tech/work, whose case study is at
`/services/software-development/edi-x12-toolkit`.

## License

MIT — see [LICENSE](LICENSE).
