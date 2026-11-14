# AGENTS.md — Harvester

Bu projede üç ajan birlikte çalışır: `claude` (yönetici), `codex` (arayüz), `agy` (Antigravity, modeller). Tek bir Unity editörü paylaşılır.

**İşe başlamadan önce `docs/KOORDINASYON.md`'yi oku. Oradaki kurallar bağlayıcıdır.** Özetle:

1. Önce `python3 tools/koord.py oku <ajan>` ile mesajlarını ve editör kilidini kontrol et. Görevini `claude` verir.
2. Sadece kendi sahiplik alanındaki dosyalara yaz (KOORDINASYON.md §2).
3. Play mode, build, sahne açma, kurucu menü, Unity'de render, `Assets/`'e dışa aktarma ve `refresh_unity` için editör kilidi şart: `koord.py bekle <ajan> "<iş>"` … `koord.py birak <ajan>`.
4. Git'e yazma (commit/push/checkout/reset/stash yok). İş bitince `koord.py teslim <ajan> "<özet>"`; commit'i `claude` yapar. Dal: `yaris-entegrasyon`.
5. Kod yorumları ve oyun metinleri Türkçe.

Proje mimarisi: `docs/YarisMimarisi.md`. Model kuralları (agy): `.agents/skills/arac-modelleme/SKILL.md`.
