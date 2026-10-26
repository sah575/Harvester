# Oyun tasarımı notları

İlk yazım: 27 Ekim 2026. Durum: **tartışma aşamasında**. Aşağıdaki kararlar verilmeden 1. faza başlanmayacak.

## Şu anki oyunun sorunu

Robotların tek hedefi 5 dakika saklanmak. Bu pasif bir oyun:

- En iyi strateji mısırın içinde bir köşeye çömelip beklemek. Robotlar arasında iş birliğine gerek yok.
- Harvester'ın işi sıkıcı bir tarama. Robot hareket etmezse onu bulmanın tek yolu bütün tarlayı biçmek.
- Ölen oyuncu turun geri kalanını izleyerek geçiriyor.

Bu türün başarılı örnekleri (Dead by Daylight, Friday the 13th, Among Us) kaçan tarafa saklanırken yapması gereken görevler vererek çözüyor. Görev onları açığa çıkmaya zorluyor, avcı da görevlerin etrafında pusu kuruyor.

## Çekirdek değişiklik önerisi: görevli kaçış

**Robotlar kazanır:** tarlaya dağılmış 5 sinyal vericiden 3'ünü onarıp açılan kaçış noktasına (ör. tarla kenarındaki kamyon ya da ışınlayıcı) ulaşırlarsa.

**Harvester kazanır:** robotların çoğunu (ör. 3'te 2'sini) yok ederse ya da süre bitene kadar kaçışı engellerse.

Bu döngü kendi gerilimini üretir:

- **Onarım ses ve ışık çıkarır.** Verici onarılırken anteni yanıp söner, harvester bunu uzaktan görür. Robot ilerlemek için risk almak zorunda.
- **Biçilen mısır saklanma alanını küçültür.** Harvester vericilerin etrafını biçerek onları "açık alana" çevirebilir. Mevcut biçme sistemi böylece daha anlamlı olur.
- **Süre harvester'ın lehine işler.** Robotlar bekleyerek kazanamaz, harekete geçmek zorunda kalır.

## Grup halinde oynama

1. **Kurtarma:** öldürülen robotun çekirdeği (kafa parçası) yerde kalır. Bir takım arkadaşı onu bir vericiye taşırsa robot yeniden kurulur. Parçalı robot modeline görsel olarak çok iyi oturur, ölen oyuncu izlemek yerine oyuna dönebilir.
2. **İki kişilik görevler:** bazı vericiler iki robotun aynı anda iki şalteri tutmasını ya da ağır bir parçanın iki robotla taşınmasını gerektirir.
3. **Dikkat dağıtma:** bir robot harvester'ı peşine takarken diğerleri onarır. Stamina ve zıplama böylece kritik bir kaynağa dönüşür; ek mekanik gerekmez, görevlerle kendiliğinden ortaya çıkar.
4. **Takım iletişimi:** basit "ping" sistemi (buraya gel / harvester burada / verici burada).

## İki tarafa da araç

- **Harvester:** 60 saniyede bir radar taraması (yakındaki robotları 2 sn gösterir), kısa turbo, robotları korkutan korna.
- **Robotlar:** tek kullanımlık EMP (harvester'ı 3 sn durdurur, görev ödülü olarak kazanılır), yem/tuzak (sahte hareket sesi), çömelme (yavaş ama mısırı eğmeden ilerleme).

Çömelme özellikle önemli: mısırın eğilmesi şu an robotun yerini ele veren doğal bir ipucu. Bunu bilinçli bir mekaniğe çevirmek gizliliğe derinlik katar.

## Ses: en ucuz en büyük kazanç

Oyunda neredeyse hiç ses yok. Gizlilik oyununda ses mekaniğin ta kendisi:

- Harvester motoru ve makara sesi mesafeye göre değişmeli; robot harvester'ı görmeden duyabilmeli.
- Mısır hışırtısı, robot ayak sesi (depar daha gürültülü), onarım sesi.
- Robot ölümünde metal parçalanma sesi.

## Grafik ve his

- **Biçme efektleri:** tabladan saçılan saman ve toz parçacıkları, biçilen tarlada iz.
- **Öldürme efekti:** parçalanmaya kıvılcım ve duman.
- **Farlar:** hacimsel ışık huzmesi (sisli gecede çok etkili); robot huzmeye girince belli olsun.
- **Robot gözleri:** karanlıkta parlayan kırmızı vizör hem güzel hem ipucu. Çömelince sönmesi bir mekanik olabilir.
- **Harita:** tarla içinde birkaç yapı (traktör, saman balyaları, sulama kanalı, korkuluk). Biçilemeyen siperler oluşturur; şu an alan düz.

## Öncelik sırası

| Faz | İçerik | Neden önce |
|---|---|---|
| 1. Çekirdek döngü | Vericiler, kaçış noktası, yeni kazanma koşulları, temel sesler | Bu olmadan diğer her şey pasif bir oyunun süsü olur |
| 2. Takım oyunu | Çekirdek taşıyıp kurtarma, iki kişilik görev, ping | Görevlerin üstüne kurulur |
| 3. Yetenekler | Radar, turbo, EMP, çömelme | Denge ayarı için önce 1–2'nin oynanması lazım |
| 4. His ve görsellik | Parçacıklar, ışık huzmesi, ses cilası, harita yapıları | Oyun eğlenceli hale gelince cilalanmaya değer |
| 5. Altyapı | Ayarlar menüsü, yapay zekâ robot botları | Botlar tek başına test ve 2 kişilik oyunları doldurmak için çok değerli |

## Verilmesi gereken kararlar

- [ ] **Robotların kazanma şekli:** görevli kaçış mı, yoksa saklanma + görev karışımı mı (ör. süre dolarsa da kazanırlar ama görevler süreyi kısaltır)?
- [ ] **Ölüm:** kalıcı mı, yoksa çekirdekten kurtarma olsun mu?
- [ ] **Hedef oyuncu sayısı:** hep 1'e 3 mü, yoksa 1'e 2 de dengeli olmalı mı? Görev sayısı buna göre ölçeklenir.
- [ ] **Tur süresi:** 5 dakika görevli bir oyun için kısa olabilir; 7–8 dakika daha uygun olabilir.

## Bugünkü durum (referans)

- Harvester son hızı ~17.5 m/s; tam hızda dönüş yarıçapı ~40 m; tabla 14.4 m genişliğinde.
- Robot yürüme 12 m/s, depar 20 m/s; depar staminası 5 sn; düz kaçışta ortalama ~15 m/s.
- Tarla 200×200 m, 2 m'lik hücreler; tur 5 dk; 2–4 oyuncu.
- Makara dönerken tablaya giren robot ölür ve 19 parçaya ayrılır; tablanın üstünden zıplayarak kaçılabilir; ölen robot izleyici kamerasına geçer.
