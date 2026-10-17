# Third-party notices

Timbratune itself is under the terms in [`LICENSE.md`](LICENSE.md). The parts below come from other
projects and stay under their own licenses.

## Euphonia (MIT)

Timbratune started as a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia). Parts of its
metric definitions, zones, texts, theme colours and data format come from there.

```
MIT License

Copyright (c) 2026 Rachel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Data and fonts

| What | Where | License |
|---|---|---|
| VCTK Corpus reference voices (CSTR, University of Edinburgh) | `src/Reyfen.Timbratune/Assets/reference/`, test fixtures | CC BY 4.0, see [`ATTRIBUTION.md`](src/Reyfen.Timbratune/Assets/reference/ATTRIBUTION.md) |
| Noto Color Emoji subset | `src/Reyfen.Timbratune.Android/Assets/Fonts/` | SIL OFL 1.1, see [`TimbratuneEmoji-LICENSE.txt`](src/Reyfen.Timbratune.Android/Assets/Fonts/TimbratuneEmoji-LICENSE.txt) |
| Inter font (through `Avalonia.Fonts.Inter`) | bundled in the app | SIL OFL 1.1 |

## Libraries

These packages are linked into the app. Their full license texts ship inside each NuGet package.

| Package | License |
|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (and its Desktop, Android, Fluent theme and Inter font packages) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | MIT |
| [SoundFlow](https://github.com/LSXPrime/SoundFlow) (with miniaudio) | MIT |

## Not included

- **FFmpeg** (LGPL 2.1 or later) is never shipped with Timbratune. It runs as a separate program, either one already on
  the PATH or one the user chooses to download, only to decode imports.
- **Praat** (GPLv3) is never shipped with or linked into Timbratune, and no Praat source code is in it. The tests can
  optionally run the separate Praat program as an oracle.
