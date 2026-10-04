using System;
using UnityEngine;
using UnityEngine.UI;

namespace IK.UI {
    /// <summary>Title screen. dev.1 only needs Input Test, Settings and About.</summary>
    public class MainMenu : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onInputTest, onSettings, onViewer;

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "MainMenu", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.05f, 0.06f, 0.09f, 1f));
            UIKit.Text(Root, "title", new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(900, 90),
                       Loc.T("app.title"), 64, TextAnchor.MiddleCenter, Skin.Accent);
            UIKit.Text(Root, "subtitle", new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(900, 44),
                       "dev.2 — " + Loc.T("menu.viewer"), 24, TextAnchor.MiddleCenter, Skin.Text);

            UIKit.Button(Root, "Viewer", new Vector2(0.5f, 0.5f), new Vector2(0, 96), new Vector2(420, 90),
                         Loc.T("menu.viewer"), () => onViewer?.Invoke(), 30);
            UIKit.Button(Root, "InputTest", new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(420, 90),
                         Loc.T("menu.inputTest"), () => onInputTest?.Invoke(), 30);
            UIKit.Button(Root, "Options", new Vector2(0.5f, 0.5f), new Vector2(0, -104), new Vector2(420, 90),
                         Loc.T("menu.settings"), () => onSettings?.Invoke(), 30);

            UIKit.Text(Root, "footer", new Vector2(0.5f, 0f), new Vector2(0, 42), new Vector2(1100, 36),
                       Loc.Arabic
                         ? "محرّك Ikemen GO (MIT) · موارد screenpack برخصة CC BY 3.0"
                         : "Ikemen GO engine (MIT) · screenpack art CC BY 3.0",
                       20, TextAnchor.MiddleCenter);
        }

        public void SetVisible(bool v) { if (Root != null) Root.gameObject.SetActive(v); }
        public bool Visible => Root != null && Root.gameObject.activeSelf;
    }
}
