# Dinamik içerik bug'ları (kullanıcı geri bildirimi + canlı doğrulama)

Kullanıcı haklı: dinamik durumları (çok kart, dolu potion, süre) test etmeden "tamam" demiştim.
Canlı oyunda dev komutlarıyla (`dev draw N`, `dev potion X`, `dev energy N`) zorlanıp doğrulandı.

## 1. Savaş: el fanı taşıyor + enerji orbı kapanıyor
- 10 kartlık elde holder gpos x: **-46 → 1174** (canvas 1129) → iki taraftan da EKRAN DIŞI, dış kartlar kırpık.
- Fan HandPosHelper'da ±610 (1220 geniş) yayılıyor — bu 1920 landscape için; portrait 1129'a sığmıyor.
- Enerji orbı canvas (100,1908), sol kartlar (92,1995) onu KAPLIYOR. HandRaise=170 de katkıda bulunmuş.
- Fix yönü: fan x-yayılımını portrait için sıkıştır (~×0.62), kart ölçeğini büyük elde biraz küçült,
  enerjiyi fandan temiz bir noktaya taşı (sol kenar / fan üstü). Piles + End Turn her zaman görünür kalmalı.

## 2. Üst HUD bar: sıkışık + süre çakışması + potion ölçeklenmiyor
Runtime geometri (savaşta):
- Portrait(16) HP(120,179w) Gold(323,138w) Potion(485,221w=3slot) RoomIcon(735) Floor(788) Boss(858)
- **TimerContainer(873, 164w → 873-1037)** Map(873,80w) Deck(953,80w) Pause(1033,80w)
- Timer gösterilince Map(873-953) VE Deck(953-1033) ile ÇAKIŞIR → kullanıcının gördüğü "süre üstüne binmiş".
- Tüm bar 1129'a margin'siz tıka basa; daha fazla potion slotu her şeyi iter.
- Fix yönü: portrait'e reflow — süreyi çakışmayan yere al/gizle, potion satırını slot sayısına göre
  konumla, sağ kümeyi (Map/Deck/Pause) yeniden diz, genel bir margin bırak.

## 3. Harita: kesik kesik, aralar siyah
- Parşömen NMapBg = VBox(MapTop/MapMid/MapBot, her biri 1129x1080), pos(0,-1540), TheMap(y=-600) ile kayar.
- Scroll şeklinin şeffaf kenarları var → bölümler arası + etraf boşluk.
- Eklediğim gradyan gap'te VAR ama srgb(11,20,9) ≈ **neredeyse siyah** (0.02-0.06 renk aralığı çok koyu).
- Fix yönü: gradyanı görünür mağara tonuna PARLAT (srgb ~40-70). Ayrıca parşömeni sürekli yapmak
  (MapMid'i tile/stretch) mümkünse gap'leri tamamen kapatır (workflow ajanı tasarlıyor).

## Yaklaşım
3 alt-sistem paralel inceletiliyor (workflow wf_7f00a0be-a02). Fix tasarımları gelince
hepsi birlikte uygulanıp tek rebuild ile canlı test edilecek; her biri dinamik içerikle (10 kart,
dolu potion) doğrulanacak.
