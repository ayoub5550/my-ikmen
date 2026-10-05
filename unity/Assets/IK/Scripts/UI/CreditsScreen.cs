using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// Credits / About: the attribution the CC BY 3.0 screenpack licence requires in-game
    /// (artists listed in the screenpack's LICENCE.txt, packed into the APK and read at run
    /// time so the text cannot drift from the licence file), the Ikemen GO MIT notice, the UI
    /// font licence and the placeholder-content warning of THIRD_PARTY_NOTICES.md.
    /// Drag to scroll; Back returns to the title.
    /// </summary>
    public class CreditsScreen : FrontEndScreen {
        public Action onBack;
        public Text Body { get; private set; }
        public string LicenceText { get; private set; } = "";

        public override void Build(RectTransform parent) {
            CreateView(parent, "Credits", "OptionBG");
            var t = UIKit.Text(View.Top, "title", new Vector2(0f, 1f), MotifView.Ui(View.Width / 2f, 50f), new Vector2(900, 60),
                               Loc.T("fe.credits"), 40, TextAnchor.MiddleCenter, Skin.Highlight);
            // scroll view
            var viewport = UIKit.Rect(View.Top, "viewport", new Vector2(0f, 1f), MotifView.Ui(View.Width / 2f, 400f), new Vector2(1100, 560));
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.55f);
            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 1400f);
            Body = UIKit.Text(content, "body", new Vector2(0.5f, 1f), new Vector2(0f, -700f), new Vector2(1040, 1400), "", 22,
                              TextAnchor.UpperLeft, Skin.Text);
            Body.horizontalOverflow = HorizontalWrapMode.Wrap;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.viewport = viewport;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            UIKit.Button(View.Top, "Back", new Vector2(0f, 1f), MotifView.Ui(100f, 48f), new Vector2(170f, 64f), Loc.T("common.back"), () => Back(), 26);
            FinishBuild();
        }

        protected override void OnShow() {
            var lic = new ResourcesSource(MotifAssets.DataGroup).Read("LICENCE.txt");
            LicenceText = lic != null ? MugenDef.DecodeText(lic) : "";
            var sb = new StringBuilder();
            sb.AppendLine("Fist Forge — built on my-ikmen, an Android fighting game on Unity, re-implementing the Ikemen GO / M.U.G.E.N engine in C# (MIT).");
            sb.AppendLine();
            sb.AppendLine("Ikemen GO engine (behavioural reference) — MIT licence, © the Ikemen GO contributors. https://github.com/ikemen-engine/Ikemen-GO");
            sb.AppendLine();
            sb.AppendLine("Screenpack (title, select, VS, lifebars, sounds) — https://github.com/ikemen-engine/Ikemen-GO-Screenpack");
            if (LicenceText.Length > 0) sb.AppendLine(LicenceText.Trim());
            else sb.AppendLine("Screenpack art and sounds: CC BY 3.0 — Ohmga Shironeko, SuperFromND, President Devon, Rurouni, Shiyo Kakuge, Cylia Margatroid, Miguel Young.");
            sb.AppendLine();
            sb.AppendLine("Kung Fu Man and the Mountainside Temple stage: Elecbyte sample content, development placeholders only.");
            sb.AppendLine("Amiri font (UI labels): SIL Open Font Licence 1.1.");
            // UIKit.Text shapes Arabic; this body is English (licence texts are kept in their own language)
            Body.text = sb.ToString();
        }

        void Back() {
            MotifAssets.PlaySnd(View.Motif.Title.CancelSnd);
            onBack?.Invoke();
        }

        public override void OnMenuKey(MenuKey key) { if (key == MenuKey.Cancel || key == MenuKey.Confirm) Back(); }
    }
}
