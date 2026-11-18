# Third-party notices

OpenPad's own code is offered under GPL-3.0-only. Bundled dependencies retain their original licenses and copyright notices.

| Dependency | Version | License / notice |
| --- | --- | --- |
| NAudio and its component assemblies | 2.2.1 | [MIT](licenses/NAudio.txt) |
| NAudio.Vorbis | 1.5.0 | [MIT](licenses/NAudio.Vorbis.txt) |
| NVorbis | 0.10.4 | [MIT](licenses/NVorbis.txt) |
| Concentus | 2.2.2 | [BSD and associated codec notices](licenses/Concentus.txt) |
| Concentus.Oggfile | 1.0.7 | [MIT](licenses/Concentus.Oggfile.txt) |

The installed edition downloads a missing .NET Desktop Runtime from Microsoft. The portable ZIP bundles the Windows x64 .NET runtime under its original license and notices, copied from the runtime packs into `licenses/dotnet-runtime-LICENSE.txt`, `licenses/dotnet-runtime-NOTICES.txt` and `licenses/dotnet-desktop-LICENSE.txt`. It is not relicensed under OpenPad's GPL license. See [Microsoft .NET](https://dotnet.microsoft.com/) and the [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

VB-CABLE is a separate system driver downloaded from its publisher when required; it is not embedded in either distribution and is not portable. See [VB-Audio](https://vb-audio.com/Cable/) for its terms.

No user audio, local library, private paths, account credentials, or test recordings are included in the repository or installer.
