# Third-party notices

sleepy is distributed with the components below. Each remains under its own
licence.

The list itself lives in `src/Sleepy.Core/ThirdPartyNotices.json` and is embedded
in the binaries, so the notices travel with them: `sleepy --license` prints them
as a table, and sleepy-GUI lays them out under **About**. This file is the same
list for anyone reading the repository, and a test fails if the two disagree.

| Component | Licence | Used by |
| --- | --- | --- |
| .NET runtime and libraries | MIT | both |
| YamlDotNet | MIT | both, for YAML input |
| Uno Platform | Apache-2.0 | sleepy-GUI |
| SkiaSharp | MIT | sleepy-GUI, rendering |
| HarfBuzzSharp | MIT | sleepy-GUI, text shaping |
| CommunityToolkit.Mvvm | MIT | sleepy-GUI |
| CommonServiceLocator | MS-PL | sleepy-GUI, via Uno |
| Tmds.DBus.Protocol | MIT | sleepy-GUI, Linux desktop |
| LibVLCSharp | LGPL-2.1-or-later | sleepy-GUI, unused |

Apache-2.0 requires that its notice travels with redistributions; LGPL-2.1
additionally requires that recipients can relink the covered component.
LibVLCSharp arrives as a transitive dependency of Uno's `MediaPlayerElement`
support, which sleepy does not use: it ships as an unreferenced assembly and can
be deleted from the published folder without affecting the app.

Full licence texts are available from each project:

- .NET — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- YamlDotNet — https://github.com/aaubry/YamlDotNet/blob/master/LICENSE.txt
- Uno Platform — https://github.com/unoplatform/uno/blob/master/License.md
- SkiaSharp / HarfBuzzSharp — https://github.com/mono/SkiaSharp/blob/main/LICENSE.md
- CommunityToolkit.Mvvm — https://github.com/CommunityToolkit/dotnet/blob/main/License.md
- CommonServiceLocator — https://github.com/unitycontainer/commonservicelocator/blob/master/LICENSE
- Tmds.DBus — https://github.com/tmds/Tmds.DBus/blob/main/COPYING
- LibVLCSharp — https://code.videolan.org/videolan/LibVLCSharp/-/blob/3.x/LICENSE
