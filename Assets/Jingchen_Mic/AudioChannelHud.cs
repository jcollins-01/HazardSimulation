using UnityEngine;
using UnityEngine.UI;

namespace Jingchen.Mic
{
    internal sealed class AudioChannelHud : MonoBehaviour
    {
        private Text label;
        private string previous;

        internal static AudioChannelHud Create(Transform head)
        {
            var root = new GameObject("Audio Channel Status", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(head, false);
            root.transform.localPosition = new Vector3(0f, -0.19f, 0.85f);
            root.transform.localScale = Vector3.one * 0.0006f;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 105f);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var background = root.AddComponent<Image>();
            background.color = new Color(0.02f, 0.03f, 0.05f, 0.8f);
            background.raycastTarget = false;
            var hud = root.AddComponent<AudioChannelHud>();
            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(root.transform, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            hud.label = textObject.AddComponent<Text>();
            hud.label.alignment = TextAnchor.MiddleCenter;
            hud.label.fontSize = 24;
            hud.label.resizeTextForBestFit = true;
            hud.label.resizeTextMinSize = 18;
            hud.label.resizeTextMaxSize = 24;
            hud.label.supportRichText = false;
            hud.label.raycastTarget = false;
            hud.label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return hud;
        }

        internal void Show(AudioChannelNetwork network, bool held, bool available)
        {
            string channel = network != null && network.Ready
                ? ((char)('A' + network.LocalChannel)).ToString() : "-";
            string state;
            Color color;
            if (!available) { state = "MIC OFF"; color = Color.gray; }
            else if (network == null || !network.Ready)
            {
                state = "CONNECTING - MIC OFF";
                color = new Color(1f, 0.8f, 0.3f);
            }
            else if (held && network.HasFloor)
            {
                state = "TALKING - RELEASE A TO FINISH";
                color = new Color(0.4f, 1f, 0.55f);
            }
            else if (network.ActiveSpeakerClientId >= 0)
            {
                state = "LISTENING TO " + (char)('A' + network.ActiveChannel) +
                    (held ? " - WAITING" : " - BUSY");
                color = new Color(1f, 0.8f, 0.3f);
            }
            else if (held)
            {
                state = "REQUESTING - WAIT TO SPEAK";
                color = new Color(1f, 0.8f, 0.3f);
            }
            else { state = "READY"; color = Color.white; }
            string text = "CHANNEL " + channel + " | " + state +
                "\nHOLD RIGHT A: TALK    TAP LEFT X: CHANNEL";
            if (text != previous) { label.text = text; previous = text; }
            label.color = color;
        }
    }
}
