# Ölçülen performans sonuçları

1080p pencereli development Mono player; editör play modunda değil. Her koşuda ayna açık, üç bot ve host ile dört araç başlar. İlk iki yarış saniyesi, menü/yükleme, yerel oyuncunun elendiği ve aynanın görünmediği kareler örnek dışıdır. Normal yarışlar arasında tekrar başlanarak 60 saniye aktif örnek toplanır. Yaşayan araç sayısı CSV/JSON içinde ayrıca kayıtlıdır; her karede dört canlı araç varsayılmaz.

| Koşu | Gerçek GPU / API | Kalite / ölçek | Örnek sn | CPU ana p95 ms | GPU p95 ms | Kare p95 ms | Draw p50 | Üçgen p50 | GC p50 / p99 B | 4 canlı kare % |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| [p0-intel](sonuclar/p0-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Ultra / 1.000 | 60.0 | 3.78 | desteklenmiyor | 10.32 | 351.00 | 189755.00 | 8214.00 / 15582.00 | 93.8 |
| [p0-nvidia](sonuclar/p0-nvidia.json) | NVIDIA GeForce RTX 3060 Laptop GPU/PCIe/SSE2 / OpenGLCore | Ultra / 1.000 | 60.0 | 3.62 | desteklenmiyor | 11.77 | 351.00 | 189731.00 | 8214.00 / 15658.00 | 94.1 |
| [p1-intel](sonuclar/p1-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Ultra / 0.500 | 60.0 | 3.94 | desteklenmiyor | 10.06 | 350.00 | 189756.00 | 8214.00 / 15666.00 | 94.2 |
| [p1-nvidia](sonuclar/p1-nvidia.json) | NVIDIA GeForce RTX 3060 Laptop GPU/PCIe/SSE2 / OpenGLCore | Ultra / 0.500 | 60.0 | 3.87 | desteklenmiyor | 7.89 | 351.00 | 189756.00 | 8214.00 / 15606.00 | 93.2 |

Yüzdelikler sıralanmış kare örneklerinde nearest-rank yöntemiyle hesaplanır. FrameTimingManager desteklenmezse CPU ana/render için ProfilerRecorder işaretçisi kullanılır; geçersiz ölçüm -1 ve sıfır geçerli örnekle raporlanır. GPU ölçümü CPU süresinden türetilmez. Ham CSV ve tam player günlükleri Builds/reports/<etiket>/ altında git dışındadır. Başka GPU, API, kalite, ölçek veya yaşayan araç dağılımı olan koşular birebir eşdeğer kabul edilmemelidir.
