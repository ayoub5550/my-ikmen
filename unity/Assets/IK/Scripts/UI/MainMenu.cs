using System;
using UnityEngine;
using UnityEngine.UI;

namespace IK.UI {
    /// <summary>
    /// The Developer menu (dev.5; it was the title screen in dev.1-dev.4): the dev.4 fight, the
    /// training room, the character viewer, the input test and the settings. Reached from
    /// Options → Developer; the rendered fixture drives it by these button names.
    /// </summary>
    public class MainMenu : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onInputTest, onSettings, onViewer, onTraining, onFight, onBack;

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "MainMenu", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.05f, 0.06f, 0.09f, 1f));
            UIKit.Text(Root, "title", new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(900, 90),
                       Loc.T("app.title"), 64, TextAnchor.MiddleCenter, Skin.Accent);
            UIKit.Text(Root, "subtitle", new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(900, 44),
                       Loc.T("fe.developer") + " — dev.1 … dev.4", 24, TextAnchor.MiddleCenter, Skin.Text);
            // dev.5: this menu is the Developer submenu (Options → Developer); Back = title
            UIKit.Button(Root, "Back", new Vector2(0f, 1f), new Vector2(110, -44), new Vector2(180, 64),
                         Loc.T("common.back"), () => onBack?.Invoke(), 26);

            // dev.4: the fight is the first entry; the training room stays below it
            UIKit.Button(Root, "Fight", new Vector2(0.5f, 0.5f), new Vector2(0, 196), new Vector2(420, 90),
                         Loc.T("menu.fight"), () => onFight?.Invoke(), 30);
            UIKit.Button(Root, "Training", new Vector2(0.5f, 0.5f), new Vector2(0, 96), new Vector2(420, 90),
                         Loc.T("menu.training"), () => onTraining?.Invoke(), 30);
            UIKit.Button(Root, "Viewer", new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(420, 90),
                         Loc.T("menu.viewer"), () => onViewer?.Invoke(), 30);
            UIKit.Button(Root, "InputTest", new Vector2(0.5f, 0.5f), new Vector2(0, -104), new Vector2(420, 90),
                         Loc.T("menu.inputTest"), () => onInputTest?.Invoke(), 30);
            UIKit.Button(Root, "Options", new Vector2(0.5f, 0.5f), new Vector2(0, -204), new Vector2(420, 90),
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
