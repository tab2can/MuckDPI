# MuckDPI

**Windows için derin paket incelemesi (DPI) aşma aracı.** Muck Store’dan kurulur, başlatılır ve durdurulur. `.cmd` yok — modları Program Ayarları’ndaki başlatma argümanlarından verirsiniz.

[![Release](https://img.shields.io/github/v/release/tab2can/MuckDPI?style=flat-square&color=dcb06a)](https://github.com/tab2can/MuckDPI/releases)
![Windows 10 and 11](https://img.shields.io/badge/Windows-10%20%26%2011-161c26?style=flat-square&labelColor=dcb06a)
![Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-6ecf9a?style=flat-square)
![Muck Store](https://img.shields.io/badge/Muck%20Store-com.tab2can.muckdpi-9aa4b7?style=flat-square)

<p align="center">
  <img src="docs/icon.png" width="96" height="96" alt="MuckDPI">
</p>

MuckDPI, ISS’lerin belirli siteleri sınıflandırıp kesmek için kullandığı **pasif ve aktif DPI**’ya karşı paket parçalama, Host manipülasyonu ve sahte istekler uygular. WinDivert sürücüsünü yükler; bu yüzden **yönetici izni** ister. Başladığında konsol penceresi gizlenir; süreç arka planda kalır.

Kaynak, [ValdikSS / GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) (Apache-2.0) üzerine kuruludur. Bu depo o kodu **MuckDPI** adıyla yeniden markalar, Muck Store manifest’i ile paketler ve komut dosyası yerine mağaza başlatma argümanlarını kullanır.

---

## Muck Store’dan kurulum

1. [Muck Store](https://github.com/tab2can/MuckStore) uygulamasını açın.
2. Discover → GitHub alanına `tab2can/MuckDPI` yapıştırın (veya `muck-store` konusunda arayın).
3. Doğrulama raporunu okuyun. `admin`, `network`, `autostart` ve `filesystem` izinleri listelenir.
4. Güven diyaloğunu onaylayın. Kurulum `%LOCALAPPDATA%\MuckStore\programs\com.tab2can.muckdpi\{sürüm}\` altına iner.
5. Kütüphane → **Başlat**. UAC sorulur (mağazada “yöneticiyi hatırla” açıksa sonraki açılışlarda atlanır). Konsol penceresi açılmaz.
6. İlk başarılı başlatma, `MuckDPI` Windows servisini `start=auto` ile kurar (eski `sc create` ile aynı). Bundan sonra Windows açılışında kendiliğinden gelir. Muck Store’daki **Start with Windows** anahtarı yönetici programlarda çalışmaz; onu kapatın.
7. Launch arguments içine `start= "auto"` **yazmayın** — o `sc create` sözdizimidir, MuckDPI bayrağı değildir.

Sideload ile denerken: Ayarlar → Geliştirici → klasörü yükle. Sideload, GitHub attestation’ını atlar.

Günlük: `%LOCALAPPDATA%\MuckStore\logs\com.tab2can.muckdpi.log`

---

## Başlatma argümanları

Muck Store, `muckdpi.exe` üzerine Program Ayarları’ndaki **Launch arguments** metnini ekler. **Boş bırakırsanız** eski Türkiye servis komutuyla aynı profil kullanılır (konsol gizli, arka planda):

`-5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253`

Kütüphane → ⋯ → Ayarlar → Launch arguments.

| Amaç | Yapıştırılacak metin |
| --- | --- |
| Varsayılan (Türkiye DNS + TTL 5) | *(boş)* |
| Sık kullanılan (frag + QUIC + DNS) | `-e 1 -q --reverse-frag --wrong-chksum --frag-by-sni --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253` |
| Modern, QUIC kapalı | `-9` |
| DNS yönlendirme ile -9 | `-9 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253` |
| Yalnız -5 (otomatik TTL) | `-5` |
| Yanlış SEQ | `-6` |
| Yanlış checksum | `-7` |
| SEQ + checksum | `-8` |

DNS yönlendirmesi deneyseldir. Tarayıcıda **Güvenli DNS / DNS over HTTPS** (ör. NextDNS) açmak çoğu senaryoda daha temizdir.

Sık kullanılan ek bayraklar:

```
--blacklist hosts.txt
--max-payload 1200
--frag-by-sni
-q
```

`--blacklist` dosyası kurulum klasörüne göreli olmalıdır. Tam liste için `muckdpi.exe -h` veya aşağıdaki referans.

---

## How it works

**Passive DPI** replies faster than the real server (HTTP 302 / TCP RST). Packets with a small IP ID are dropped.

**Active DPI** sits in the path. MuckDPI uses TCP fragmentation, Host-header tricks, and fake requests (wrong checksum, old SEQ/ACK, or a low TTL) so the classifier sees garbage while the destination still gets a valid flow.

The process stays up until Muck Store stops it. Stdout goes to the store log, not a console window.

---

## Supported arguments

```
Usage: muckdpi.exe [OPTION...]
 -p          block passive DPI
 -q          block QUIC/HTTP3
 -r          replace Host with hoSt
 -s          remove space between host header and its value
 -m          mix Host header case (test.com -> tEsT.cOm)
 -f <value>  set HTTP fragmentation to value
 -k <value>  enable HTTP persistent (keep-alive) fragmentation and set it to value
 -n          do not wait for first segment ACK when -k is enabled
 -e <value>  set HTTPS fragmentation to value
 -a          additional space between Method and Request-URI (enables -s, may break sites)
 -w          try to find and parse HTTP traffic on all processed ports (not only on port 80)
 --port        <value>    additional TCP port to perform fragmentation on (and HTTP tricks with -w)
 --ip-id       <value>    handle additional IP ID (decimal, drop redirects and TCP RSTs with this ID)
 --dns-addr    <value>    redirect UDP DNS requests to the supplied IP address (experimental)
 --dns-port    <value>    redirect UDP DNS requests to the supplied port (53 by default)
 --dnsv6-addr  <value>    redirect UDPv6 DNS requests to the supplied IPv6 address (experimental)
 --dnsv6-port  <value>    redirect UDPv6 DNS requests to the supplied port (53 by default)
 --dns-verb               print verbose DNS redirection messages
 --blacklist   <txtfile>  circumvention only for host names from this file (repeatable)
 --allow-no-sni           still act if TLS SNI is missing while --blacklist is on
 --frag-by-sni            fragment the TLS packet right before the SNI value
 --set-ttl     <value>    fake request with this TTL (dangerous; prefer --blacklist)
 --auto-ttl    [a1-a2-m]  fake request with auto TTL (default 1-4-10; also --min-ttl 3)
 --min-ttl     <value>    minimum TTL distance for fake requests
 --wrong-chksum           fake request with an incorrect TCP checksum
 --wrong-seq              fake request with SEQ/ACK in the past
 --native-frag            split packets without shrinking the window
 --reverse-frag           native frag, reversed send order
 --fake-from-hex <value>  fake payload from hex (repeatable)
 --fake-with-sni <value>  fake Firefox-like ClientHello for this SNI (repeatable)
 --fake-gen <value>       generate that many random fake packets (up to 30)
 --fake-resend <value>    send each fake packet this many times (default 1)
 --max-payload [value]    skip TCP payloads larger than this (default 1200 if set)

LEGACY modesets:
 -1          -p -r -s -f 2 -k 2 -n -e 2
 -2          -p -r -s -f 2 -k 2 -n -e 40
 -3          -p -r -s -e 40
 -4          -p -r -s

Modern modesets:
 -5          -f 2 -e 2 --auto-ttl --reverse-frag --max-payload
 -6          -f 2 -e 2 --wrong-seq --reverse-frag --max-payload
 -7          -f 2 -e 2 --wrong-chksum --reverse-frag --max-payload
 -8          -f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload
 -9          -f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload -q   (default)
```

---

## Build from source

GNU Make + [mingw-w64](https://www.mingw-w64.org). The only compile-time dependency is [WinDivert](https://github.com/basil00/Divert) 2.2.0-D.

x86_64:

```bash
make -C src CPREFIX=x86_64-w64-mingw32- BIT64=1 \
  WINDIVERTHEADERS=/path/to/WinDivert-2.2.0-D/include \
  WINDIVERTLIBS=/path/to/WinDivert-2.2.0-D/x64
```

Ship `muckdpi.exe` next to `WinDivert.dll` and `WinDivert64.sys`. GitHub Releases are produced by [`.github/workflows/release.yml`](.github/workflows/release.yml): MinGW build, zip, GitHub Artifact Attestation, upload. Muck Store will not accept a zip attached by hand.

After the first `v1.0.0` tag, paste the SHA-256 from the Actions log into `muck.json` → `install.assets[].sha256` and push to `main`. Then add the GitHub topic **`muck-store`** so Discover can find the repo.

Validate the manifest from a Muck Store checkout:

```bash
node cli/muck-validate.mjs path/to/MuckDPI
```

---

## Known issues

- Very old Windows 7 images cannot load WinDivert without SHA-256 driver signatures (KB3033929) — use a current Windows 10/11 install.
- Intel/Qualcomm Killer “Advanced Stream Detect” fights the filter; disable it.
- QUIK trading software: start QUIK first, then MuckDPI.

---

## License and credit

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

- [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) by ValdikSS — the engine this repository rebrands
- [WinDivert](https://github.com/basil00/Divert) by basil00 — packet divert driver
- [BlockCheck](https://github.com/ValdikSS/blockcheck) contributors — DPI behaviour research
- [Muck Store](https://github.com/tab2can/MuckStore) — install, start/stop, updates, launch arguments

Similar projects: [zapret](https://github.com/bol-van/zapret), [ByeDPI](https://github.com/hufrea/byedpi), [PowerTunnel](https://github.com/krlvm/PowerTunnel), [SpoofDPI](https://github.com/xvzc/SpoofDPI).
