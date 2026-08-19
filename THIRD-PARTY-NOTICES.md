# Third-party notices

sleepy is distributed with the components below. Each remains under its own
licence; the list is reproduced inside both apps (`sleepy --license` and the
About dialog in sleepy-GUI) so the notices travel with the binaries.

| Component | Licence | Used by |
| --- | --- | --- |
| .NET runtime and libraries (Microsoft) | MIT | both |
| YamlDotNet | MIT | both (YAML input) |
| Uno Platform (Uno.WinUI, Uno.Resizetizer, Uno.Toolkit) | Apache-2.0 | sleepy-GUI |
| SkiaSharp, SkiaSharp.Skottie, SkiaSharp.SceneGraph, SkiaSharp.Resources | MIT | sleepy-GUI (rendering) |
| HarfBuzzSharp | MIT | sleepy-GUI (text shaping) |
| CommunityToolkit.Mvvm | MIT | sleepy-GUI |
| CommonServiceLocator | MS-PL | sleepy-GUI (via Uno) |
| Tmds.DBus.Protocol | MIT | sleepy-GUI (Linux desktop integration) |
| LibVLCSharp | LGPL-2.1-or-later | sleepy-GUI (pulled in by Uno's media support) |

Apache-2.0 requires that its notice travels with redistributions; LGPL-2.1
additionally requires that recipients can relink the covered component. The
LibVLCSharp assembly is a transitive dependency of Uno's `MediaPlayerElement`
support, which sleepy does not use — it ships as an unreferenced assembly that
can be deleted from the published folder without affecting the app.

Full licence texts are available from each project:

- .NET — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- YamlDotNet — https://github.com/aaubry/YamlDotNet/blob/master/LICENSE.txt
- Uno Platform — https://github.com/unoplatform/uno/blob/master/License.md
- SkiaSharp / HarfBuzzSharp — https://github.com/mono/SkiaSharp/blob/main/LICENSE.md
- CommunityToolkit.Mvvm — https://github.com/CommunityToolkit/dotnet/blob/main/License.md
- CommonServiceLocator — https://github.com/unitycontainer/commonservicelocator/blob/master/LICENSE
- Tmds.DBus — https://github.com/tmds/Tmds.DBus/blob/main/COPYING
- LibVLCSharp — https://code.videolan.org/videolan/LibVLCSharp/-/blob/3.x/LICENSE
