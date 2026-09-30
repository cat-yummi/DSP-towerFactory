using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TowerFactory
{
    /// <summary>
    /// 在运输塔窗口右上角加一个"自动填原料"按钮。
    /// </summary>
    [HarmonyPatch(typeof(UIStationWindow))]
    public static class UIStationWindowPatch
    {
        private static Button _autoFillButton;

        [HarmonyPostfix]
        [HarmonyPatch("OnStationIdChange")]
        public static void OnStationIdChange_Postfix(UIStationWindow __instance)
        {
            EnsureButton(__instance);
            if (_autoFillButton == null)
            {
                return;
            }
            StationComponent station = GetStation(__instance);
            bool visible = __instance.active && station != null && !station.isCollector && !station.isVeinCollector;
            _autoFillButton.gameObject.SetActive(visible);
        }

        private static StationComponent GetStation(UIStationWindow window)
        {
            PlanetTransport transport = window.transport;
            int stationId = window.stationId;
            if (transport == null || stationId <= 0 || stationId >= transport.stationCursor)
            {
                return null;
            }
            StationComponent station = transport.stationPool[stationId];
            return station != null && station.id == stationId ? station : null;
        }

        private static void EnsureButton(UIStationWindow window)
        {
            if (_autoFillButton != null || window.windowTrans == null)
            {
                return;
            }

            GameObject btnObj = new GameObject("tower-factory-autofill");
            btnObj.transform.SetParent(window.windowTrans, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(1f, 1f);
            btnRect.anchorMax = new Vector2(1f, 1f);
            btnRect.pivot = new Vector2(1f, 1f);
            btnRect.anchoredPosition = new Vector2(-20f, -36f);
            btnRect.sizeDelta = new Vector2(100f, 22f);

            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.color = new Color(0.24f, 0.55f, 0.65f, 0.9f);

            _autoFillButton = btnObj.AddComponent<Button>();
            var colors = _autoFillButton.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            _autoFillButton.colors = colors;
            _autoFillButton.onClick.AddListener(() => OnAutoFillClick(window));

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            Text text = textObj.AddComponent<Text>();
            text.text = "塔厂：填原料";
            text.font = window.titleText != null ? window.titleText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private static void OnAutoFillClick(UIStationWindow window)
        {
            StationComponent station = GetStation(window);
            if (station == null)
            {
                return;
            }
            bool filled = StationProductionPatch.TryAutoFill(window.transport, station, out string message);
            UIRealtimeTip.Popup(message, !filled, 0);
            if (filled)
            {
                window.stationId = window.stationId;
            }
        }
    }
}
