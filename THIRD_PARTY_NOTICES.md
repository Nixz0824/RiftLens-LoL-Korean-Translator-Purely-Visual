# Third-party notices

RiftLens source code is licensed under the repository's [MIT License](LICENSE). The components and data below remain under their respective licenses and are not relicensed as RiftLens code. Redistributable license texts used by the current build are kept in [`licenses/`](licenses/).

## Runtime components

| Component | Version | Use | License / terms |
| --- | ---: | --- | --- |
| [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) PP-OCRv5 Mobile models | official Paddle 3.0 inference models, converted locally to ONNX | Primary local text detection plus Korean-English and Chinese-English recognition | [Apache-2.0](licenses/Apache-2.0.txt); model provenance and hashes are recorded below |
| [Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime) | 1.23.2 | CPU-only local inference for bundled PP-OCRv5 models | [MIT](licenses/ONNX-Runtime-MIT.txt) |
| [Tesseract .NET wrapper](https://github.com/charlesw/tesseract) | 5.2.0 | Local fallback OCR integration; package also provides native Tesseract and Leptonica binaries | [Apache-2.0](licenses/Apache-2.0.txt) for the wrapper; bundled native components retain their own licenses |
| [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) | 5.x native binary from the wrapper package | Local Korean-English OCR fallback | [Apache-2.0](licenses/Apache-2.0.txt) |
| [Leptonica](https://github.com/DanBloomberg/leptonica) | 1.82.0 native binary from the wrapper package | Image processing used by Tesseract | [BSD 2-Clause](licenses/Leptonica-BSD-2-Clause.txt) |
| [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) | bundled `kor` and `eng` models | Korean and English fallback OCR data | [Apache-2.0](licenses/Apache-2.0.txt) |
| [NAudio](https://github.com/naudio/NAudio) and its 2.0.0 subpackages | 2.0.0 | Audio playback and Windows audio integration | [MIT](licenses/NAudio-MIT.txt) |
| [Microsoft Edge WebView2 SDK](https://developer.microsoft.com/microsoft-edge/webview2/) | 1.0.4078.44 | Hosts the locally packaged HTML/CSS/JS interface | [Microsoft package license](licenses/Microsoft-WebView2.txt); WebView2 Runtime is separately governed by Microsoft terms |

The Tesseract package also depends on Microsoft `System.*` compatibility packages. A self-contained .NET publication includes Microsoft .NET runtime files under the license and notices shipped with that runtime. Redistributors should preserve the notices produced with the published output and review the exact dependency graph of the version they distribute.

## Bundled PP-OCRv5 model provenance

The bundled ONNX files are local format conversions of official PaddleOCR/PaddleX inference artifacts. No training data is bundled. Conversion changes the serialization format, not the model purpose or upstream license.

| Bundled asset | Official source | Source SHA-256 | Bundled SHA-256 |
| --- | --- | --- | --- |
| `ppocrv5_mobile_det.onnx` | [`PP-OCRv5_mobile_det_infer.tar`](https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_det_infer.tar) | `50446E5D01AC2A73D5319C89513281F6578414C888C602F9AF13F93FEEFFFC58` | `C8D9B07063420CE5365C74E42532DE48238FEEEEDCDB7A330B195708BC38A93F` |
| `ppocrv5_mobile_rec_korean.onnx` | [`korean_PP-OCRv5_mobile_rec_infer.tar`](https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/korean_PP-OCRv5_mobile_rec_infer.tar) | `A6261F800AD335AA6EF77B6F7C4DCEDFD5E11B95F4D70982D41EE31C2ACE5E66` | `48F0554EA902F18D2C7735ABE4A15595D340064680FAE4E9DD6DECAA640538BB` |
| `ppocrv5_mobile_rec_chinese.onnx` | [`PP-OCRv5_mobile_rec_infer.tar`](https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_rec_infer.tar) | `566B9512B34E34A9F0DB54D87B51FA5A0B9ED2CF1AB7E49728CC0B8B5A64F414` | `BCB195E3463EB9E46EF419B8A01EA4729577DE5FD63C64F0A762E43BD64256E7` |
| `ppocrv5_korean_dict.txt` | [PaddleOCR Korean dictionary](https://github.com/PaddlePaddle/PaddleOCR/blob/main/ppocr/utils/dict/ppocrv5_korean_dict.txt) | `A88071C68C01707489BAA79EBE0405B7BEB5CCA229F4FC94CC3EF992328802D7` | same as source |
| `ppocrv5_dict.txt` | [PaddleOCR Chinese dictionary](https://github.com/PaddlePaddle/PaddleOCR/blob/main/ppocr/utils/dict/ppocrv5_dict.txt) | `D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B` | same as source |

The conversion used Paddle2ONNX 2.1.0 with ONNX opset 17. The checked-in ONNX graphs pass the ONNX checker and load with the CPU execution provider. Paddle2ONNX is conversion tooling and is not included in the RiftLens runtime.

### NAudio MIT notice

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Riot Games data and trademarks

The offline champion-name and game-term mappings were derived from Riot Games Data Dragon `ko_KR`, `zh_CN`, and `en_US` locale data, version 16.14.1. Names are aligned only by stable champion, ability, summoner-spell, item, and rune IDs. The compact embedded catalog contains localized names, not game artwork or full raw Data Dragon payloads.

League of Legends, Riot Games, champion names, Data Dragon data, and associated properties belong to Riot Games, Inc. or their respective rights holders. They are not covered by the RiftLens MIT license.

RiftLens isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

## Online services

RiftLens may connect to an OpenAI Chat Completions-compatible endpoint selected by the user and, when enabled, to Microsoft Edge online speech. These are services, not bundled open-source components. Their availability, output, billing, privacy, and permitted use are governed by the selected provider's current terms.

## Redistribution note

This file is a practical inventory, not legal advice. Anyone publishing a binary or modified fork should inspect the actual packages included in that build, retain required copyright and license texts, and comply with the current terms of any online service used.
