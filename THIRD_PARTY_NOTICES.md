# Third-party notices

RiftLens source code is licensed under the repository's [MIT License](LICENSE). The components and data below remain under their respective licenses and are not relicensed as RiftLens code. Redistributable license texts used by the current build are kept in [`licenses/`](licenses/).

## Runtime components

| Component | Version | Use | License / terms |
| --- | ---: | --- | --- |
| [Tesseract .NET wrapper](https://github.com/charlesw/tesseract) | 5.2.0 | Managed OCR integration; package also provides native Tesseract and Leptonica binaries | [Apache-2.0](licenses/Apache-2.0.txt) for the wrapper; bundled native components retain their own licenses |
| [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) | 5.x native binary from the wrapper package | Local OCR engine | [Apache-2.0](licenses/Apache-2.0.txt) |
| [Leptonica](https://github.com/DanBloomberg/leptonica) | 1.82.0 native binary from the wrapper package | Image processing used by Tesseract | [BSD 2-Clause](licenses/Leptonica-BSD-2-Clause.txt) |
| [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) | bundled `kor` and `eng` models | Korean and English OCR data | [Apache-2.0](licenses/Apache-2.0.txt) |
| [NAudio](https://github.com/naudio/NAudio) and its 2.0.0 subpackages | 2.0.0 | Audio playback and Windows audio integration | [MIT](licenses/NAudio-MIT.txt) |
| [Microsoft Edge WebView2 SDK](https://developer.microsoft.com/microsoft-edge/webview2/) | 1.0.4078.44 | Hosts the locally packaged HTML/CSS/JS interface | [Microsoft package license](licenses/Microsoft-WebView2.txt); WebView2 Runtime is separately governed by Microsoft terms |

The Tesseract package also depends on Microsoft `System.*` compatibility packages. A self-contained .NET publication includes Microsoft .NET runtime files under the license and notices shipped with that runtime. Redistributors should preserve the notices produced with the published output and review the exact dependency graph of the version they distribute.

### NAudio MIT notice

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Riot Games data and trademarks

The offline champion-name mapping was derived from Riot Games Data Dragon `ko_KR`, `zh_CN`, and `en_US` locale data, version 16.14.1, and aligns names by stable champion ID.

League of Legends, Riot Games, champion names, Data Dragon data, and associated properties belong to Riot Games, Inc. or their respective rights holders. They are not covered by the RiftLens MIT license.

RiftLens isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

## Online services

RiftLens may connect to an OpenAI Chat Completions-compatible endpoint selected by the user and, when enabled, to Microsoft Edge online speech. These are services, not bundled open-source components. Their availability, output, billing, privacy, and permitted use are governed by the selected provider's current terms.

## Redistribution note

This file is a practical inventory, not legal advice. Anyone publishing a binary or modified fork should inspect the actual packages included in that build, retain required copyright and license texts, and comply with the current terms of any online service used.
