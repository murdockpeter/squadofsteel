using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SquadOfSteelMod.Scale
{
    public static class SquadScaleSelectionUI
    {
        static GameObject s_root;
        static TMP_FontAsset s_font;
        static bool s_timePaused;
        static float s_previousTimeScale = 1f;

        public static bool IsBlocking => s_root != null && s_root.activeSelf;

        public static void ResetForScenario()
        {
            Hide();
        }

        public static void Shutdown()
        {
            Hide();
            if (s_root != null)
                UnityEngine.Object.Destroy(s_root);

            s_root = null;
        }

        public static void TryShowIfNeeded()
        {
            if (!SquadScaleRuntime.NeedsSelection)
            {
                Hide();
                return;
            }

            if (s_root == null)
            {
                var canvas = UIManager.instance?.mainCanvas;
                if (canvas == null)
                    return;

                EnsureFont();
                Create(canvas);
            }

            if (!s_root.activeSelf)
                s_root.SetActive(true);

            s_root.transform.SetAsLastSibling();
            if (!s_timePaused)
            {
                s_previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                s_timePaused = true;
            }
        }

        static void Select(string profileId)
        {
            if (!SquadScaleRuntime.SelectProfile(profileId))
            {
                Debug.LogWarning($"[SquadOfSteel][Scale] Could not activate profile '{profileId}'.");
                return;
            }

            Hide();
        }

        static void Hide()
        {
            if (s_root != null && s_root.activeSelf)
                s_root.SetActive(false);

            if (s_timePaused)
            {
                Time.timeScale = s_previousTimeScale;
                s_timePaused = false;
            }
        }

        static void EnsureFont()
        {
            if (s_font != null)
                return;

            s_font = UIManager.instance?.playerMoneyAmount_Text?.font;
            if (s_font == null)
                s_font = TMP_Settings.defaultFontAsset;
        }

        static void Create(Canvas canvas)
        {
            s_root = new GameObject("SquadOfSteel.ScaleSelection", typeof(RectTransform), typeof(Image));
            var rootRect = s_root.GetComponent<RectTransform>();
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            s_root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(s_root.transform, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(720f, 0f);

            panel.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.98f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 26, 26);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(panel.transform, "Squad of Steel Scale", 30f, FontStyles.Bold, 52f);
            AddText(
                panel.transform,
                "Choose how Squad of Steel should interpret this scenario. Unit ranges, movement, visibility, damage, and the unit database will not be changed.",
                18f,
                FontStyles.Normal,
                76f);

            foreach (var profile in SquadScaleRuntime.Profiles)
            {
                var selectedProfile = profile;
                AddButton(
                    panel.transform,
                    $"{profile.DisplayName}\n<size=16>{profile.Description}</size>",
                    () => Select(selectedProfile.Id));
            }
        }

        static void AddText(Transform parent, string value, float size, FontStyles style, float preferredHeight)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = s_font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
        }

        static void AddButton(Transform parent, string label, Action action)
        {
            var go = new GameObject("ProfileButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 78f;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.17f, 0.27f, 0.43f, 1f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());

            var colors = button.colors;
            colors.highlightedColor = new Color(0.25f, 0.40f, 0.64f, 1f);
            colors.pressedColor = new Color(0.12f, 0.20f, 0.34f, 1f);
            button.colors = colors;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 6f);
            labelRect.offsetMax = new Vector2(-18f, -6f);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.font = s_font;
            text.text = label;
            text.fontSize = 21f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
        }
    }
}
