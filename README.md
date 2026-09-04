# MuckDPI

ISS’nize göre ayarlanan, Windows için DPI aşımı. [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) çalışsa bile bazen site açılır, video / hikâye / API / ses açılmaz. MuckDPI bunu bilinçli olarak kapatmak için yazıldı.

[Muck Store](https://github.com/tab2can/MuckStore) üzerinden kurulur (`topic:muck-store`, `tab2can/MuckDPI`).

![MuckDPI](docs/icon.png)

## Neden GoodbyeDPI-Turkey ile aynı dil?

Türkiye’de çalışan kombinasyon belgelenmiş durumda: `goodbyedpi.exe -5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253`. MuckDPI bunu varsayılan profil yapar, üstüne Superonline alt.1–3 ve TTNet `-9` profillerini GUI’den seçtirir.

| | GoodbyeDPI-Turkey | MuckDPI 1.1 |
| --- | --- | --- |
| DNS | `77.88.8.8:1253` paket yönlendirme | Aynı NAT (DoH yedek) |
| Desync | `-5` + `--set-ttl 5` | Aynı; ISS’ye göre alt profiller |
| Kapsam | Tüm HTTPS | Tüm HTTPS, banka/e-Devlet hariç |
| Superonline | 6 ayrı `.cmd` | alt.1 / alt.2 / alt.3 menüde |
| YouTube zayıfsa | `-9` | Güçlü (-9) + QUIC düşürme |
| Arayüz | Konsol / servis | Türkçe GUI, sihirbaz, tepsi |

Kaynak kodu kopyalanmadı; komut satırı profilleri ve DNS NAT davranışı herkese açık belgelerden uygulandı.

IP seviyesinde (hiç paket yok, yalnızca zaman aşımı) engel DPI ile çözülmez. O durumda VPN gerekir; uygulama bunu gizlemez.

## Ne yapar?

- **DNS:** UDP/53 sorgularını Yandex `77.88.8.8:1253` adresine kaydırır (ISS 53. portu kesse bile). DoH isteğe bağlı yedek.
- **Aktif DPI:** Türkiye önerileni `-5` (2 bayt + ters parça) + sahte TTL 5
- **Pasif DPI:** sahte RST / yönlendirme paketlerini düşürür
- **Kapsam:** banka ve e-Devlet hariç tüm HTTPS (yalnızca ana alan adı yetmez)
- **QUIC:** varsayılan kapalı; Güçlü (-9) profilinde düşürülür

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
- Türkiye profilleri [GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey) komut dosyalarındaki herkese açık argümanlardan esinlenir (`-5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253`). Kaynak kodu kopyalanmaz.
- MuckDPI kaynak kodu **MIT**.

Bu yazılım sansür / DPI aşımı içindir. Ağınızın ve yasaların sorumluluğu size aittir.

## Muck Store geliştirici notu

`muck.json` alanları [developer-guide](https://github.com/tab2can/MuckStore/blob/main/docs/developer-guide.md) ile uyumludur. Sürüm etiketi `v1.0.0` → `.github/workflows/release.yml` `muckdpi.zip` üretir, attestation ekler, Release’e yükler. Hash’i Actions logundan `install.assets[].sha256` alanına yazıp tekrar etiketleyin.

---

# MuckDPI (English)

Windows DPI circumvention that adapts to your ISP. GoodbyeDPI often opens the front page while CDNs, APIs, video and voice stay broken. MuckDPI ships service host packs, DoH, and an on-line strategy wizard.

Requires administrator rights (WinDivert). IP-level blocks need a VPN; DPI tricks cannot invent a route that does not exist.
