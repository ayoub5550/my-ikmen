using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace IK.EditorTools {
    /// <summary>
    /// dev.8: adds the Firebase Test Lab game-loop intent filter
    /// (<c>com.google.intent.action.TEST_LOOP</c>, mime type application/javascript) to the Unity
    /// activity of the generated Gradle project, so <c>gcloud firebase test android run --type game-loop</c>
    /// can start <see cref="IK.App.DeviceProbe"/>. A normal launch never carries that action.
    /// </summary>
    public class IKGameLoopManifest : IPostGenerateGradleAndroidProject {
        public int callbackOrder => 10;

        const string Filter =
            "            <intent-filter>\n" +
            "                <action android:name=\"com.google.intent.action.TEST_LOOP\" />\n" +
            "                <category android:name=\"android.intent.category.DEFAULT\" />\n" +
            "                <data android:mimeType=\"application/javascript\" />\n" +
            "            </intent-filter>\n";

        public void OnPostGenerateGradleAndroidProject(string path) {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[IK] game-loop: no manifest at " + manifest); return; }
            string xml = File.ReadAllText(manifest);
            if (xml.Contains("com.google.intent.action.TEST_LOOP")) return;
            int main = xml.IndexOf("android.intent.action.MAIN");
            int close = main >= 0 ? xml.IndexOf("</intent-filter>", main) : -1;
            if (close < 0) { Debug.LogWarning("[IK] game-loop: no launcher intent-filter"); return; }
            int at = close + "</intent-filter>".Length;
            xml = xml.Insert(at, "\n" + Filter);
            File.WriteAllText(manifest, xml);
            Debug.Log("[IK] game-loop intent filter added to " + manifest);
        }
    }
}
