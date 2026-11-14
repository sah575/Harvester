# Harvester relay sunucusu

Online oyun için odaları tutan ve paketleri oyuncular arasında taşıyan küçük bir UDP sunucusu.
Oyun mantığı çalıştırmaz: odayı kuran oyuncu NGO host'udur, relay sadece aracıdır.

- **HarvesterRelay/**: sunucu (.NET 8, LiteNetLib 1.3.5).
- **RelayTestClient/**: uçtan uca test ve yük testi.
- **Ortak protokol:** `Assets/scripts/Net/RelayProtocol.cs`. Unity ve sunucu aynı dosyayı derler; mesaj düzeni değişirse `Version` artırılır.

## Nasıl çalışır

1. Host `Create` ile bağlanır, sunucu 6 karakterlik bir davet kodu verir (`RoomCreated`).
2. İstemci `Join` ile kodu yollar. Sunucu ona bir numara verir (1, 2, 3…; numaralar tekrar kullanılmaz) ve host'a `PeerJoined` bildirir.
3. Veri her zaman host ile istemci arasında akar. İstemcinin paketine sunucu gönderen numarasını ekler; host'un paketinde hedef numarası vardır. Teslim şekli (güvenilir/güvenilmez/sıralı) korunur.
4. Host ayrılırsa oda kapanır ve istemciler `RoomClosed` sebebiyle düşer.

**Korumalar:**
- Bağlantı başına hız sınırı: 1 MB/sn, 2 MB'a kadar ani yük.
- IP başına dakikada 20 hatalı kod denemesi.
- Oda ve bağlantı üst sınırı.

## Ölçümler (10 Kasım 2026)

- Ev → VDS ping: ~12 ms.
- İstemci → relay → host → relay → istemci gidiş-dönüş: p50 ~27 ms. Oyunda tek yön ~15 ms.
- 10 oda × 4 oyuncu, 30 Hz: tek çekirdeğin ~%17'si (o sırada makinede bir Bannerlord sunucusu da çalışıyordu). Boşta ~%0, bellek ~13 MB.

Olay güdümlü mod (paketler LiteNetLib'in alma iş parçacığında, kilit altında) ve her taşımadan sonra `TriggerUpdate` en iyi sonucu verdi. Bekleme döngüsü boşta bile çekirdeğin %13'ünü yiyordu.

## Komutlar

```bash
# Yerelde derle ve dene (yerel SDK .NET 6 olduğu için hedef değiştiriliyor)
cd Server/HarvesterRelay && dotnet build -p:RelayTfm=net6.0
cd Server/RelayTestClient && dotnet build -p:RelayTfm=net6.0
dotnet Server/HarvesterRelay/bin/Debug/net6.0/HarvesterRelay.dll --port 17778 &
dotnet Server/RelayTestClient/bin/Debug/net6.0/RelayTestClient.dll selftest 127.0.0.1 17778

# Canlı sunucuyu dene
dotnet Server/RelayTestClient/bin/Debug/net6.0/RelayTestClient.dll selftest 188.132.198.43 7778
dotnet Server/RelayTestClient/bin/Debug/net6.0/RelayTestClient.dll bench 10 30 188.132.198.43 7778

# VDS'e yükle (orada derler, servisi yeniden başlatır)
Server/deploy.sh

# Sunucuda
systemctl status harvester-relay
journalctl -u harvester-relay -f
```

## İlk kurulum (yapıldı, kayıt için)

- `apt install dotnet-sdk-8.0`
- `useradd --system --no-create-home --shell /usr/sbin/nologin harvester`
- Güvenlik duvarı:
  ```
  ufw allow 22/tcp
  ufw allow 7778/udp
  ufw default deny incoming
  ufw enable
  ```
- Servis dosyası `deploy/harvester-relay.service`; `deploy.sh` onu kurar.
