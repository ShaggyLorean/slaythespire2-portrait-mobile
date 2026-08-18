# Launcher tasarım sistemi (anti-slop sözleşmesi)

Kimlik: oyunun kendi dilinden ödünç — "el mürekkebiyle basılmış kutu oyunu kapağı".
Parşömen şerit, mürekkep plakalar, letterpress kenarlar. Launcher bir ARAÇ gibi
konuşur (sol hizalı, net), oyun gibi kokar (parşömen + kan kırmızısı mühür).

## Paleta (tek kaynak: LauncherComponentTheme)

| Token | Hex | Kullanım |
| --- | --- | --- |
| Ink | #0B131A | ekran zemini |
| InkSlab | #101C25 | ikincil buton/plaka dolgusu |
| InkRule | #05090D | plaka alt çizgisi (letterpress) |
| Parchment | #D9C28D | başlık şeridi, plaka üst çizgisi |
| ParchmentShade | #B49A66 | şerit alt kenarı |
| ParchmentInk | #1B1207 | parşömen üstü metin |
| Bone | #EFE6D2 | plaka üstü metin |
| BoneDim | #B9AE9B | ikincil metin/durum |
| Blood | #8E2F33 | TEK yüksek sesli şey: birincil eylem |
| BloodPressed | #6C2228 | birincil basılı |
| Ember | #E4B85A | küçük vurgular, sürüm damgası |
| Spark | #55C7D7 | nadir nokta vurguları (kule ışıkları) |

## Biçim kuralları

- Radius: 0. Her yerde. (Diyalog paneli dahil; tek istisna 2px chip yok, hiç yok.)
- Gölge yok, glow yok, degrade yok, cam/blur yok.
- Plaka anatomisi: düz dolgu + ÜSTTE 3px parşömen cetvel + ALTTA 1px InkRule.
  Kenarlıklı-şeffaf buton YOK (hayalet buton yasağı).
- Tipografi: başlık serif bold BÜYÜK HARF, gövde sans. Başlık parşömen üstünde
  mürekkep rengi. Buton metni SOL hizalı, birincilde sağda "›" ucu.
- Kompozisyon: tam kanama (full-bleed). Ortalanmış dar kolon + yan ray YASAK.
  24px yan marj; bölümler arası 12/20/32 ritmi. Kule silüeti zeminde tam boy.
- Başlık şeridi: tam genişlik parşömen bant, -1.2° elle-yapıştırılmış eğim,
  altında 1px ParchmentShade kenar. Üstünde küçük durum satırı değil; durum
  satırı bandın ALTINDA mürekkep üstünde BoneDim.
- Doğrulama/Guard bildirimi: parşömen bant klonu, Blood 2px çerçeve, metin
  ParchmentInk; detaylara gömülmez, durum satırının hemen altında belirir.
- Sürüm damgası: sağ alt, Ember, küçük, "v0.4.0-dev9" gibi; başka köşe süsü yok.

## Yasaklar (adıyla)

Mor-mavi degrade; her karta 1px gri çerçeve; üç özellik kartı; Inter başlık;
yuvarlak hap butonlar; ortalanmış her şey; yarı saydam kolon paneli; ikon
çorbası; boşluk dolduramayan dev dikey gap'ler.
