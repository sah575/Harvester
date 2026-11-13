# Ölçülen performans sonuçları

1080p pencereli development Mono player; editör play modunda değil. Her koşuda ayna açık, üç bot ve host ile dört araç başlar. İlk iki yarış saniyesi, menü/yükleme, yerel oyuncunun elendiği ve aynanın görünmediği kareler örnek dışıdır. Normal yarışlar arasında tekrar başlanarak 60 saniye aktif örnek toplanır. Yaşayan araç sayısı CSV/JSON içinde ayrıca kayıtlıdır; her karede dört canlı araç varsayılmaz.

| Koşu | Gerçek GPU / API | Kalite / ölçek | Örnek sn | CPU ana p95 ms | GPU p95 ms | Kare p95 ms | Draw p50 | Üçgen p50 | GC p50 / p99 B | 4 canlı kare % |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| [p0-intel](sonuclar/p0-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Ultra / 1.000 | 60.0 | 3.78 | desteklenmiyor | 10.32 | 351.00 | 189755.00 | 8214.00 / 15582.00 | 93.8 |
| [p0-nvidia](sonuclar/p0-nvidia.json) | NVIDIA GeForce RTX 3060 Laptop GPU/PCIe/SSE2 / OpenGLCore | Ultra / 1.000 | 60.0 | 3.62 | desteklenmiyor | 11.77 | 351.00 | 189731.00 | 8214.00 / 15658.00 | 94.1 |
| [p1-intel](sonuclar/p1-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Ultra / 0.500 | 60.0 | 3.94 | desteklenmiyor | 10.06 | 350.00 | 189756.00 | 8214.00 / 15666.00 | 94.2 |
| [p1-nvidia](sonuclar/p1-nvidia.json) | NVIDIA GeForce RTX 3060 Laptop GPU/PCIe/SSE2 / OpenGLCore | Ultra / 0.500 | 60.0 | 3.87 | desteklenmiyor | 7.89 | 351.00 | 189756.00 | 8214.00 / 15606.00 | 93.2 |
| [p2-intel](sonuclar/p2-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.46 | desteklenmiyor | 16.77 | 159.00 | 50247.00 | 8202.00 / 18590.00 | 93.3 |
| [p2-nvidia](sonuclar/p2-nvidia.json) | NVIDIA GeForce RTX 3060 Laptop GPU/PCIe/SSE2 / OpenGLCore | Yüksek / 0.500 | 60.0 | 2.17 | desteklenmiyor | 2.18 | 351.00 | 189764.00 | 8202.00 / 15554.00 | 91.8 |
| [p3-intel](sonuclar/p3-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 6.34 | desteklenmiyor | 16.79 | 135.00 | 28049.00 | 14200.00 / 30222.00 | 93.9 |
| [p3-final-intel](sonuclar/p3-final-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.62 | desteklenmiyor | 16.79 | 135.00 | 28049.00 | 8202.00 / 19802.00 | 94.3 |
| [p4-intel](sonuclar/p4-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.43 | desteklenmiyor | 16.78 | 135.00 | 28049.00 | 8202.00 / 19802.00 | 93.2 |
| [p5-intel](sonuclar/p5-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.89 | desteklenmiyor | 16.80 | 135.00 | 28049.00 | 8202.00 / 19802.00 | 93.8 |
| [p5-final-intel](sonuclar/p5-final-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.83 | desteklenmiyor | 16.80 | 135.00 | 28049.00 | 8202.00 / 19776.00 | 93.8 |
| [p6-intel](sonuclar/p6-intel.json) | Mesa Intel(R) Iris(R) Xe Graphics (ADL GT2) / OpenGLCore | Düşük / 0.500 | 60.0 | 5.43 | desteklenmiyor | 16.76 | 123.00 | 28049.00 | 8202.00 / 19822.00 | 94.3 |

Yüzdelikler sıralanmış kare örneklerinde nearest-rank yöntemiyle hesaplanır. FrameTimingManager desteklenmezse CPU ana/render için ProfilerRecorder işaretçisi kullanılır; geçersiz ölçüm -1 ve sıfır geçerli örnekle raporlanır. GPU ölçümü CPU süresinden türetilmez. Ham CSV ve tam player günlükleri Builds/reports/<etiket>/ altında git dışındadır. Başka GPU, API, kalite, ölçek veya yaşayan araç dağılımı olan koşular birebir eşdeğer kabul edilmemelidir.
