# Optimizasyon sonrası kullanıcı testi

Bu liste manuel kabul testi içindir; aşağıdaki maddelerin tamamı otomatik test edilmiş sayılmaz. Linux player: `Builds/LinuxTest/Harvester.x86_64`. Giriş sahnesi Lobby'dir. İki makineyle test etmek, aynı bilgisayarda açılan iki kopyadan farklı ağ/GPU koşullarını da sınar.

- Ana menü → Garaj: sahip olunan araçlar arasında geç, bir jant/parça seç, rengi değiştir ve performans geliştir. Önizlemede değişiklik hemen görünmeli. Fareyi basılı tutup aracı döndür; bırakınca önizleme davranışını kontrol et. Kaydet, çıkıp tekrar gir; seçim korunmalı.
- Mağaza: araçları sağ/sol değiştir, performans çubuklarına bak, satın al ve garajda seç. Satın alma/modifiye bağlıyken açılmamalı.
- Ayarlar: Düşük/Orta/Yüksek, görüntü ölçeği ve kamera varsayılanını değiştir. Tekrar açılışta tercih korunmalı; Lethal piksel/dither görünümü kalmalı. Düşük 60 FPS sınırı ve daha seyrek ayna yenilemesi kullanır.
- Oyna → Çevrimiçi → Oda kur. İkinci kopya/makine davet koduyla katılsın. Host yarışı başlatsın. Araç/parça/renk her iki tarafta doğru görünmeli.
- Yarışta C ile dört kamera, kaput/kokpit; M ile ayna; F3 ile ağ ayrıntılarını kontrol et. Yakındaki farlar ve fren emisyonu görünmeli. LOD geçişinde harvester parçaları yerinden kopmamalı.
- Yarış bitince iki taraf da bağlantı açıkken oda paneline dönmeli; ikinci yarışı başlatabilmelisin.
- Intel GPU ile Düşük/1080p, RTX ile Yüksek/1080p birer sürüş yap. Akıcılık, ayna ve görüntüye ilişkin geri bildirim ver. Ölçümlerdeki FPS sınırlaması nedeniyle yalnız FPS sayısına bakma; takılma hissini de not et.

**Bekleyen kullanıcı kararları:** Enkaz/collider isteği karşılanmadı; kullanıcı şimdilik bununla uğraşılmamasını istedi. Harvester tehdit/avlama modeli ve canlı araba–araba çarpışması da değiştirilmedi. Bunlar kabul edilmiş veya çözülmüş sayılmamalı. Geçici test bayrağı ile yapılacak sürüşler normal çarpışmalı performans koşularıyla eşdeğer değildir; ayrıntı `docs/DEVIR_CLAUDE.md` içindedir.
