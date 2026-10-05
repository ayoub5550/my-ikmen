using System.Collections.Generic;
using UnityEngine;
using IK.Settings;

namespace IK.UI {
    /// <summary>
    /// UI strings in Arabic and English. <see cref="Shape"/> is applied by UIKit to every
    /// label, so Arabic is rendered joined and right-to-left by the legacy Text renderer.
    /// Numbers always stay left-to-right (they are Latin digits in both languages).
    /// </summary>
    public static class Loc {
        public static bool Arabic { get; private set; } = true;

        public static void Apply(Language language) {
            switch (language) {
                case Language.Arabic: Arabic = true; break;
                case Language.English: Arabic = false; break;
                default:
                    Arabic = Application.systemLanguage == SystemLanguage.Arabic;
                    break;
            }
        }

        public static string Shape(string text) => ArabicShaper.Shape(text);

        static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]> {
            // key                         { Arabic, English }
            { "app.title",                 new[] { "إكمن", "IKMEN" } },
            { "menu.inputTest",            new[] { "اختبار الأوامر", "Input Test" } },
            { "menu.settings",             new[] { "الإعدادات", "Settings" } },
            { "menu.about",                new[] { "حول اللعبة", "About" } },
            { "menu.quit",                 new[] { "خروج", "Quit" } },
            { "common.back",               new[] { "رجوع", "Back" } },
            { "common.on",                 new[] { "تشغيل", "On" } },
            { "common.off",                new[] { "إيقاف", "Off" } },
            { "common.reset",              new[] { "إعادة ضبط الصفحة", "Reset this page" } },
            { "common.save",               new[] { "حفظ", "Save" } },
            { "common.cancel",             new[] { "إلغاء", "Cancel" } },
            { "page.controls",             new[] { "التحكّم", "Controls" } },
            { "page.game",                 new[] { "اللعب", "Game" } },
            { "page.audio",                new[] { "الصوت", "Audio" } },
            { "page.video",                new[] { "الصورة", "Video" } },
            { "page.language",             new[] { "اللغة", "Language" } },
            { "page.about",                new[] { "حول", "About" } },
            { "ctl.directionMode",         new[] { "نمط الاتجاه", "Direction mode" } },
            { "ctl.dpad",                  new[] { "لوحة اتجاهات", "D-pad" } },
            { "ctl.floating",              new[] { "عصا عائمة", "Floating stick" } },
            { "ctl.fixed",                 new[] { "عصا ثابتة", "Fixed stick" } },
            { "ctl.buttonSize",            new[] { "حجم الأزرار", "Button size" } },
            { "ctl.opacity",               new[] { "شفافية الأزرار", "Controls opacity" } },
            { "ctl.slide",                 new[] { "التمرير بين الأزرار", "Slide-to-press" } },
            { "ctl.macro",                 new[] { "أزرار مركّبة", "Macro buttons" } },
            { "ctl.showDW",                new[] { "إظهار زري D و W", "Show D / W buttons" } },
            { "ctl.haptics",               new[] { "الاهتزاز", "Haptics" } },
            { "ctl.onScreen",              new[] { "أزرار الشاشة", "On-screen controls" } },
            { "ctl.light",                 new[] { "خفيف", "Light" } },
            { "ctl.strong",                new[] { "قوي", "Strong" } },
            { "ctl.auto",                  new[] { "تلقائي", "Auto" } },
            { "ctl.always",                new[] { "دائماً", "Always" } },
            { "ctl.never",                 new[] { "أبداً", "Never" } },
            { "ctl.buttonAssist",          new[] { "مساعدة الضغط المتزامن", "Button assist" } },
            { "ctl.socd",                  new[] { "حل تعارض الاتجاهات", "SOCD resolution" } },
            { "ctl.sensitivity",           new[] { "حساسية العصا", "Stick sensitivity" } },
            { "ctl.deadzone",              new[] { "المنطقة الميتة", "Dead zone" } },
            { "ctl.editLayout",            new[] { "تعديل التخطيط…", "Edit layout…" } },
            { "ctl.preset",                new[] { "تخطيط جاهز", "Preset" } },
            { "ctl.slot",                  new[] { "خانة الحفظ", "Save slot" } },
            { "game.difficulty",           new[] { "الصعوبة", "AI difficulty" } },
            { "game.life",                 new[] { "نسبة الحياة", "Life %" } },
            { "game.time",                 new[] { "زمن الجولة", "Round time" } },
            { "game.wins",                 new[] { "جولات الفوز", "Rounds to win" } },
            { "game.speed",                new[] { "سرعة اللعب", "Game speed" } },
            { "game.autoGuard",            new[] { "صدّ تلقائي", "Auto guard" } },
            { "audio.master",              new[] { "الصوت العام", "Master volume" } },
            { "audio.bgm",                 new[] { "الموسيقى", "Music volume" } },
            { "audio.sfx",                 new[] { "المؤثرات", "SFX volume" } },
            { "video.fps",                 new[] { "حد الإطارات", "FPS cap" } },
            { "video.renderScale",         new[] { "دقة العرض", "Render scale" } },
            { "video.filter",              new[] { "تنعيم البكسل", "Pixel filtering" } },
            { "video.sharp",               new[] { "حاد", "Sharp" } },
            { "video.smooth",              new[] { "ناعم", "Smooth" } },
            { "video.showFps",             new[] { "إظهار الإطارات", "Show FPS" } },
            { "lang.arabic",               new[] { "العربية", "العربية" } },
            { "lang.english",              new[] { "English", "English" } },
            { "lang.system",               new[] { "لغة النظام", "System language" } },
            { "test.title",                new[] { "اختبار الأوامر", "Input test" } },
            { "test.hint",                 new[] { "جرّب: ربع دائرة أمام + لكمة", "Try: QCF + punch" } },
            { "layout.title",              new[] { "تعديل التخطيط", "Edit layout" } },
            { "layout.hint",               new[] { "اسحب الأزرار، والمقياس بالشريط", "Drag the buttons, resize with the slider" } },
            { "layout.resetDefault",       new[] { "استعادة الافتراضي", "Reset to default" } },
            { "layout.invalid",            new[] { "تخطيط غير صالح", "Invalid layout" } },
            { "menu.viewer",               new[] { "عارض الشخصية", "Character viewer" } },
            { "viewer.prev",               new[] { "السابقة", "Prev" } },
            { "viewer.next",               new[] { "التالية", "Next" } },
            { "viewer.play",               new[] { "تشغيل", "Play" } },
            { "viewer.pause",              new[] { "إيقاف", "Pause" } },
            { "viewer.palette",            new[] { "لوحة الألوان", "Palette" } },
            { "viewer.boxes",              new[] { "مربعات الإصابة", "Clsn boxes" } },
            { "viewer.sound",              new[] { "صوت", "Sound" } },
            { "viewer.failed",             new[] { "تعذّر تحميل الشخصية", "Character load failed" } },
            { "viewer.hint",               new[] { "تصفّح حركات الشخصية", "Browse the character's actions" } },
            { "viewer.stats",              new[] { "{0} سبرايت · {1} لوحة ألوان · {2} حركة · {3} صوت · حُمّلت في {4} مل.ث", "{0} sprites · {1} palettes · {2} actions · {3} sounds · loaded in {4} ms" } },
            { "viewer.info",               new[] { "حركة {0} · عنصر {1}/{2} · سبرايت {3} · زمن {4} · Clsn1 {5} · Clsn2 {6}", "Action {0} · element {1}/{2} · sprite {3} · time {4} · Clsn1 {5} · Clsn2 {6}" } },
            { "menu.training",             new[] { "التدريب", "Training" } },
            { "training.title",            new[] { "{0} · {1} حالة · {2} أمر — محرّك C# يعمل بـ 60 نبضة/ث", "{0} · {1} states · {2} commands — C# engine at 60 ticks/s" } },
            { "training.hint",             new[] { "العب بالأزرار: الاتجاهات للحركة · x y للكمات · a b للركلات · حركة ربع دائرة + x = كف الكونغ فو", "Play with the on-screen controls: directions move · x y punch · a b kick · quarter-circle + x = Kung Fu Palm" } },
            { "training.reset",            new[] { "إعادة", "Reset" } },
            { "training.hud",              new[] { "المؤشرات", "HUD" } },
            { "training.failed",           new[] { "تعذّر تشغيل المحرّك", "Engine start failed" } },
            { "training.state",            new[] { "حالة {0} · زمن {1} · حركة {2} · عنصر {3}/{4} · تحكّم {5}", "state {0} · time {1} · anim {2} · elem {3}/{4} · ctrl {5}" } },
            { "training.vel",              new[] { "سرعة {0:0.00},{1:0.00} · موضع {2:0.0},{3:0.0} · قوة {4} · ضربات {5} · أمر {6}", "vel {0:0.00},{1:0.00} · pos {2:0.0},{3:0.0} · power {4} · hits {5} · cmd {6}" } },
            { "about.credits",             new[] { "شكر وتقدير", "Credits" } },
        };

        public static string T(string key) {
            if (Strings.TryGetValue(key, out var v)) return Arabic ? v[0] : v[1];
            return key;
        }

        /// <summary>Localised + shaped, ready for a Text component.</summary>
        public static string S(string key) => Shape(T(key));
    }
}
