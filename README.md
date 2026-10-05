# my-ikmen

لعبة قتال لأندرويد مبنية على **Unity**، مستوحاة من محرّك **Ikemen GO** (متوافق مع M.U.G.E.N) وتستخدم موارده.

An Android fighting game built with **Unity**, based on the open-source **Ikemen GO** engine and its resources.

## الحالة / Status

آخر مرحلة: **dev.7** — الأداء: إصلاح تسريب صور الخلفية وتسريب صور الحلبات، تقليل نفايات الذاكرة في المحرك بثلاثين ضعفاً، تجهيز السبرايتات مسبقاً، انتظام الإطارات، اختبار أداء وعداد إطارات داخل اللعبة، إعدادات الدقة والتنعيم تعمل، وشكل جديد لأزرار اللمس مع إصلاح اختفائها أثناء القتال. التفاصيل في `docs/DEV7.md` (والمرحلة السابقة في `docs/DEV6.md`).

Latest milestone: **dev.7** — performance (texture leaks fixed, 30x less engine garbage, sprite prewarm, frame pacing), in-game benchmark + FPS overlay, working render scale / filters, modern touch buttons now drawn above the fight. See `docs/DEV7.md` (and `docs/DEV6.md`).

## محتوى المستودع / Layout

| المسار | المحتوى |
|---|---|
| `engine/ikemen-go/` | الشيفرة المصدرية لمحرّك Ikemen GO ‏v1.0.0 (Go، رخصة MIT) — مرجع للنقل إلى Unity |
| `assets/screenpack/` | موارد اللعبة الافتراضية: الشخصيات، الساحات، القوائم، الخطوط، الأصوات، الفيديو |
| `unity/` | مشروع Unity: المحرّك المنقول إلى C#، الواجهات، القوائم وأنماط اللعب / the Unity project |
| `docs/` | خطة التطوير ووثائق كل مرحلة |
| `AGENTS.md` | دليل المطوّرين والـ agents (ابدأ من هنا) / Start here for developers and AI agents |
| `TESTING.md` | طرق الاختبار بدون هاتف / Testing layers |
| `tools/sandbox/` | إصلاحات بيئة البناء الآلية (من my-librequake) |

## الرخص / Licences

- شيفرتنا: MIT — انظر `LICENSE`.
- الموارد المنقولة تحتفظ برخصها الأصلية — انظر `THIRD_PARTY_NOTICES.md`.
- ⚠️ خطوط Elecbyte وشخصية Kung Fu Man مؤقتة فقط ويجب استبدالها قبل أي إصدار تجاري.

## Credits

Ikemen GO © Ikemen GO contributors (MIT). Screenpack art © Ohmga Shironeko, SuperFromND, President Devon, Rurouni, Shiyo Kakuge, Cylia Margatroid, Miguel Young (CC BY 3.0).
