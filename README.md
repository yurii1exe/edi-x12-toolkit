# EdiX12

[![CI](https://github.com/yurii1exe/edi-x12-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/yurii1exe/edi-x12-toolkit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

<!-- Swap the line above for these two the moment 0.1.0-alpha is pushed to nuget.org,
     and delete the "not yet on nuget.org" note below:
[![NuGet](https://img.shields.io/nuget/v/EdiX12.Core.svg?label=EdiX12.Core)](https://www.nuget.org/packages/EdiX12.Core/)
[![NuGet](https://img.shields.io/nuget/v/EdiX12.Cli.svg?label=EdiX12.Cli)](https://www.nuget.org/packages/EdiX12.Cli/)
-->


Parse, validate and generate ANSI X12 freight EDI in .NET.

Freight EDI is a solved problem that everyone re-solves badly. Most integrations start
with `text.Split('~')` and `segment.Split('*')`, work fine against the first partner, and
break the day a partner sends a file delimited with `|` and terminated with a newline —
which is entirely legal, and common.

EdiX12 handles the envelope, the delimiters and the control numbers correctly, so you work
with typed objects instead of string-splitting.

> **Not yet on nuget.org.** `0.1.0-alpha` is packed and exercised in CI — the pack job
> installs the tool from the package it just built and runs it against the samples — but
> it has not been pushed to the feed. Until it is, clone the repo and `dotnet build`. The
> two install commands in this README are written for the day it lands, not for today.

```bash
dotnet add package EdiX12.Core --prerelease   # once published
```

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

## Command line

`EdiX12.Cli` packages the same parser as a .NET global tool called `edix12`:

```bash
dotnet tool install --global EdiX12.Cli --prerelease   # once published
```

Until then, `dotnet run --project src/EdiX12.Cli -- <command>` does the same thing from a
clone. Every transcript below is real output, copied verbatim.

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

The .NET 8 SDK is the minimum, and it is enough: there is no `global.json` pinning a newer
SDK, the solution is in the classic `.sln` format, and both the test project and the CLI
target `net8.0`.
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

## License

MIT — see [LICENSE](LICENSE).
