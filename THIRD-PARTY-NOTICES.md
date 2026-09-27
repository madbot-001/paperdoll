# Third-party notices

Paperdoll ports some logic from the Space Station 14 engine and game. Those parts keep the
notices below. Files that contain ported code say so at the top. The builds also include the
libraries listed under [Libraries in the builds](#libraries-in-the-builds).

## RobustToolbox (the Space Station 14 engine)

https://github.com/space-wizards/RobustToolbox. Code contributed after 13 March 2019 is MIT
licensed (the engine's `legal.md`); only such code is ported.

```
 Copyright (c) 2019 Space Station 14 Contributors

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

## Space Station 14

https://github.com/space-wizards/space-station-14

```
MIT License

Copyright (c) 2017-2026 Space Wizards Federation

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

## The README screenshot

`.github/screenshot.png` shows sprites from
[Space Station 14](https://github.com/space-wizards/space-station-14), licensed
[CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) by their authors, named in each
sprite's `meta.json` there. The picture is under CC-BY-SA 3.0 too, not the MIT licence.

## Libraries in the builds

The ready-made builds include these libraries and the .NET runtime, so nothing else needs
installing. Each build's `licenses` folder holds the full notices of .NET, and of SkiaSharp and
HarfBuzzSharp with Skia, HarfBuzz and the libraries those use; the Windows build's also holds
ANGLE's.

| Library | Licence | Copyright |
| --- | --- | --- |
| [.NET runtime](https://github.com/dotnet/runtime) | MIT | .NET Foundation and Contributors |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia), with its DataGrid | MIT | 2013-2026 The AvaloniaUI Project |
| [SkiaSharp and HarfBuzzSharp](https://github.com/mono/SkiaSharp) | MIT | 2015-2016 Xamarin, Inc.; 2017-2018 Microsoft Corporation |
| Skia, HarfBuzz and the libraries they use | BSD, MIT, Apache 2.0 and others | see the `licenses` folder |
| [ANGLE](https://chromium.googlesource.com/angle/angle), Windows build only | BSD 3-Clause | 2018 The ANGLE Project Authors |
| [MicroCom](https://github.com/kekekeks/MicroCom) | MIT | 2021 Nikita Tsukanov |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | MIT | Tom Deseyn |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT | Antoine Aubry and contributors |

The MIT licence, as it applies to each library above that uses it, with that library's
copyright holders:

```
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
