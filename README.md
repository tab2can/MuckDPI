# MuckDPI

ISS’nize göre ayarlanan, Windows için DPI aşımı. [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) çalışsa bile bazen site açılır, video / hikâye / API / ses açılmaz. MuckDPI bunu bilinçli olarak kapatmak için yazıldı.

[Muck Store](https://github.com/tab2can/MuckStore) üzerinden kurulur (`topic:muck-store`, `tab2can/MuckDPI`).

![MuckDPI](docs/icon.png)

## Neden GoodbyeDPI’den farklı?

| Sorun | GoodbyeDPI | MuckDPI |
| --- | --- | --- |
| Her ISS aynı değil | Elle `-5`…`-9` | ASN ile ISS profili + hattınızda sihirbaz |
| Ana site açılır, özellik bozulur | Çoğu zaman yalnızca SNI | YouTube, Discord, Instagram **CDN/API** host paketleri |
| DNS zehirlemesi | İsteğe bağlı yönlendirme | DoH (1.1.1.1 / 8.8.8.8 doğrudan IP) |
| QUIC her şeyi kırar | Global `-q` | Yalnızca gereken servislerde |
| Banka / e-Devlet | Global desync bozabilir | Akıllı host listesi + hariç tutma |
| Arayüz | Konsol | Tepsi, günlük, tarama, Türkçe UI |

IP seviyesinde (hiç paket yok, yalnızca zaman aşımı) engel DPI ile çözülmez. O durumda VPN gerekir; uygulama bunu gizlemez.

## Ne yapar?

- **Pasif DPI:** sahte RST / yönlendirme paketlerini düşürür
- **Aktif DPI:** TLS ClientHello’yu SNI sınırından böler, isteğe bağlı ters sıra, sahte TTL / yanlış seq / checksum
- **DNS:** UDP/53 sorgularını Cloudflare, Google, Quad9, AdGuard veya Mullvad DoH ile yanıtlar
- **QUIC:** YouTube / Instagram / TikTok gibi servislerde TCP’ye düşürür (HTTP/3 DPI’si bozmasın diye)
- **Öğrenme:** RST görülen hostları akıllı listeye ekler

Hazır ISS profilleri: Türk Telekom, Superonline, Vodafone, TurkNet, Türksat Kablonet, Millenicom.

## Kurulum

### Muck Store (önerilen)

1. Muck Store → Discover → `tab2can/MuckDPI`
2. İzinleri okuyun (`admin` şart: WinDivert sürücüsü)
3. Install → Start

İlk sürümde `muck.json` içindeki `sha256`, GitHub Actions’ın ürettiği `muckdpi.zip` özetine güncellenmeden store kurulumu hash yüzünden durabilir. Sideload ile deneyebilirsiniz: Settings → Developer → bu klasörü yükle.

### Kaynaktan

Windows 10/11 x64, .NET 8 SDK, yönetici yetkisi.

```powershell
pwsh tools/fetch-windivert.ps1
dotnet publish src/MuckDPI/MuckDPI.csproj -c Release -r win-x64 --self-contained true -o dist/app
# yönetici olarak dist/app/MuckDPI.exe
```

## Kullanım

1. **Korumayı başlat** (UAC)
2. **ISS sihirbazı** → ISS’yi algıla → Hattımı tara
3. Servis paketlerinde kullandığınız uygulamaları açık bırakın
4. DNS koruması açık kalsın

Ayarlar `%APPDATA%\MuckDPI\settings.json` veya Muck Store `MUCK_SETTINGS_PATH`.

## Güvenlik ve lisans

- Motor **kendi kodumuz**; GoodbyeDPI kaynak kodu kopyalanmadı. Yöntemler herkese açık DPI literatürü ve zapret/GoodbyeDPI belgelerindeki tekniklerdir.
- Paket içindeki **WinDivert** LGPLv3 / GPLv2 ([basil00/WinDivert](https://github.com/basil00/WinDivert)). Lisans metni: `third_party/WinDivert.LICENSE`.
- MuckDPI kaynak kodu **MIT**.

Bu yazılım sansür / DPI aşımı içindir. Ağınızın ve yasaların sorumluluğu size aittir.

## Muck Store geliştirici notu

`muck.json` alanları [developer-guide](https://github.com/tab2can/MuckStore/blob/main/docs/developer-guide.md) ile uyumludur. Sürüm etiketi `v1.0.0` → `.github/workflows/release.yml` `muckdpi.zip` üretir, attestation ekler, Release’e yükler. Hash’i Actions logundan `install.assets[].sha256` alanına yazıp tekrar etiketleyin.

---

# MuckDPI (English)

Windows DPI circumvention that adapts to your ISP. GoodbyeDPI often opens the front page while CDNs, APIs, video and voice stay broken. MuckDPI ships service host packs, DoH, and an on-line strategy wizard.

Requires administrator rights (WinDivert). IP-level blocks need a VPN; DPI tricks cannot invent a route that does not exist.
