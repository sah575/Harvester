#!/usr/bin/env python3
"""Player'ın ölçtüğü JSON özetlerini karşılaştırma tablosuna dönüştürür; veri uydurmaz."""
import argparse,json,shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('labels',nargs='+');a=p.parse_args()
root=Path(__file__).resolve().parents[1];dest=root/'docs/performans/sonuclar';dest.mkdir(parents=True,exist_ok=True)
lines=['# Ölçülen performans sonuçları','',
       '1080p pencereli development Mono player; editör play modunda değil. Her koşuda ayna açık, üç bot ve host ile dört araç başlar. İlk iki yarış saniyesi, menü/yükleme, yerel oyuncunun elendiği ve aynanın görünmediği kareler örnek dışıdır. Normal yarışlar arasında tekrar başlanarak 60 saniye aktif örnek toplanır. Yaşayan araç sayısı CSV/JSON içinde ayrıca kayıtlıdır; her karede dört canlı araç varsayılmaz.',
       '', '| Koşu | Gerçek GPU / API | Kalite / ölçek | Örnek sn | CPU ana p95 ms | GPU p95 ms | Kare p95 ms | Draw p50 | Üçgen p50 | GC p50 / p99 B | 4 canlı kare % |',
       '|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|']
for label in a.labels:
    source=root/'Builds/reports'/label/(label+'.json')
    data=json.loads(source.read_text());shutil.copy2(source,dest/source.name)
    metrics={m['name']:m for m in data['metrics']}
    def value(key,percentile='p95'):
        m=metrics.get(key)
        return f"{m[percentile]:.2f}" if m and m['validSamples'] else 'desteklenmiyor'
    lines.append(f"| [{label}](sonuclar/{label}.json) | {data['gpu']} / {data['api']} | {data['quality']} / {data['renderScale']:.3f} | {data['capturedSeconds']:.1f} | {value('cpu_main_ms')} | {value('gpu_ms')} | {value('wall_ms')} | {value('draw_calls','p50')} | {value('triangles','p50')} | {value('gc_bytes','p50')} / {value('gc_bytes','p99')} | {100*data['fourAliveFrames']/data['frames']:.1f} |")
lines+=['','Yüzdelikler sıralanmış kare örneklerinde nearest-rank yöntemiyle hesaplanır. FrameTimingManager desteklenmezse CPU ana/render için ProfilerRecorder işaretçisi kullanılır; geçersiz ölçüm -1 ve sıfır geçerli örnekle raporlanır. GPU ölçümü CPU süresinden türetilmez. Ham CSV ve tam player günlükleri Builds/reports/<etiket>/ altında git dışındadır. Başka GPU, API, kalite, ölçek veya yaşayan araç dağılımı olan koşular birebir eşdeğer kabul edilmemelidir.']
(root/'docs/performans/Olcumler.md').write_text('\n'.join(lines)+'\n')
print('\n'.join(lines))
