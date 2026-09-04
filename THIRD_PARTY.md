# Third-party

## WinDivert

This program dynamically links [WinDivert](https://github.com/basil00/WinDivert) by basil00.

WinDivert is licensed under the GNU Lesser General Public License v3 (or later) and the GNU General Public License v2 (or later). See the license files shipped next to `WinDivert.dll` in the release zip.

MuckDPI itself is MIT. The combined binary distribution therefore includes LGPL/GPL WinDivert components; the WinDivert sources are available from the upstream project.

## Techniques

Packet desynchronization ideas (SNI split, fake TTL, wrong sequence/checksum, reverse fragments, host-list filtering, DoH) are documented by public projects including GoodbyeDPI and zapret. This repository does not copy their source code.
