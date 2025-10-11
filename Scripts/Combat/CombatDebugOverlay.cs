// =============================================================================
// Renders a slim, left-docked combat debug overlay with scrollable entries.
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SquadOfSteelMod.Combat
{
    static class CombatDebugOverlay
    {
        const int MaxEntries = 80;
        const int MaxPending = 50;

        static readonly List<string> s_pendingEntries = new List<string>();
        static readonly List<GameObject> s_entries = new List<GameObject>();

        static GameObject s_root;
        static RectTransform s_content;
        static ScrollRect s_scrollRect;
        static TMP_FontAsset s_defaultFont;
        static bool s_warnedMissingCanvas;

        public static void Initialize()
        {
            if (s_root != null)
                return;

            var hostCanvas = GetHostCanvas();
            if (hostCanvas == null)
            {
                if (!s_warnedMissingCanvas)
                {
                    Debug.LogWarning("[SquadOfSteel][Overlay] UIManager canvas not yet available; overlay deferred.");
                    s_warnedMissingCanvas = true;
                }
                return;
            }

            EnsureFont();
            CreateOverlay(hostCanvas);
            FlushPending();

            if (!SquadCombatRuntime.DebugEnabled && s_root != null)
                s_root.SetActive(false);
        }

        public static void SetVisible(bool visible)
        {
            if (!visible)
            {
                if (s_root != null)
                    s_root.SetActive(false);

                Debug.Log("[SquadOfSteel][Overlay] Hidden (debug off).");
                return;
            }

            Initialize();

            if (s_root != null)
            {
                s_root.SetActive(true);

                if (s_entries.Count == 0)
                    AddEntry("Combat debug overlay enabled.");

                Debug.Log("[SquadOfSteel][Overlay] Shown (debug on).");
            }
            else
            {
                Debug.LogWarning("[SquadOfSteel][Overlay] Could not show overlay; root is null.");
            }
        }

        public static void AddEntry(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (s_root == null)
            {
                QueuePending(message);
                Initialize();

                if (s_root == null)
                {
                    Debug.LogWarning("[SquadOfSteel][Overlay] Entry queued; overlay not yet ready.");
                    return;
                }
            }

            var entryGO = new GameObject("Entry", typeof(RectTransform));
            entryGO.transform.SetParent(s_content, worldPositionStays: false);
            var entryRect = entryGO.GetComponent<RectTransform>();
            entryRect.anchorMin = new Vector2(0f, 1f);
            entryRect.anchorMax = new Vector2(1f, 1f);
            entryRect.pivot = new Vector2(0.5f, 1f);
            entryRect.offsetMin = Vector2.zero;
            entryRect.offsetMax = Vector2.zero;

            var text = entryGO.AddComponent<TextMeshProUGUI>();
            text.font = s_defaultFont;
            text.fontSize = 20f;
            text.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.richText = false;
            text.raycastTarget = false;
            text.margin = new Vector4(12f, 4f, 12f, 8f);
            text.text = message.Replace("\r\n", "\n");

            s_entries.Add(entryGO);
            if (s_entries.Count > MaxEntries)
            {
                var toRemove = s_entries[0];
                s_entries.RemoveAt(0);
                Object.Destroy(toRemove);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(entryRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(s_content);

            if (s_scrollRect != null)
                s_scrollRect.verticalNormalizedPosition = 1f;
        }

        static void QueuePending(string message)
        {
            s_pendingEntries.Add(message);
            if (s_pendingEntries.Count > MaxPending)
                s_pendingEntries.RemoveAt(0);
        }

        static void FlushPending()
        {
            if (s_pendingEntries.Count == 0)
                return;

            foreach (var entry in s_pendingEntries)
                AddEntry(entry);

            s_pendingEntries.Clear();
        }

        static Canvas GetHostCanvas()
        {
            return UIManager.instance != null ? UIManager.instance.mainCanvas : null;
        }

        static void EnsureFont()
        {
            if (s_defaultFont != null)
                return;

            s_defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
            if (s_defaultFont == null)
                s_defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial");
            if (s_defaultFont == null)
                s_defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

            if (s_defaultFont == null && UIManager.instance != null && UIManager.instance.playerMoneyAmount_Text != null)
                s_defaultFont = UIManager.instance.playerMoneyAmount_Text.font;

            if (s_defaultFont == null)
            {
                var builtInArial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (builtInArial != null)
                    s_defaultFont = TMP_FontAsset.CreateFontAsset(builtInArial);
            }

            if (s_defaultFont == null)
                s_defaultFont = TMP_Settings.defaultFontAsset;
        }

        static void CreateOverlay(Canvas hostCanvas)
        {
            s_root = new GameObject("SquadOfSteelDebugOverlay", typeof(RectTransform));
            var rootRect = s_root.GetComponent<RectTransform>();
            rootRect.SetParent(hostCanvas.transform, worldPositionStays: false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            s_root.layer = hostCanvas.gameObject.layer;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(s_root.transform, worldPositionStays: false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0.5f);
            panelRect.anchorMax = new Vector2(0f, 0.5f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(360f, 720f);
            panelRect.anchoredPosition = new Vector2(32f, 0f);

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            panelImage.raycastTarget = false;

            var panelGroup = panel.GetComponent<CanvasGroup>();
            panelGroup.blocksRaycasts = false;
            panelGroup.interactable = false;
            panelGroup.ignoreParentGroups = true;

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, worldPositionStays: false);
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 64f);
            headerRect.anchoredPosition = Vector2.zero;

            var headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.18f, 0.28f, 0.45f, 1f);
            headerImage.raycastTarget = false;

            var title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(header.transform, worldPositionStays: false);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-20f, 0f);

            var titleText = title.AddComponent<TextMeshProUGUI>();
            titleText.font = s_defaultFont;
            titleText.text = "Squad of Steel - Combat Debug";
            titleText.fontSize = 24f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.raycastTarget = false;

            var body = new GameObject("Body", typeof(RectTransform), typeof(Image));
            body.transform.SetParent(panel.transform, worldPositionStays: false);
            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(16f, 16f);
            bodyRect.offsetMax = new Vector2(-16f, -72f);

            var bodyImage = body.GetComponent<Image>();
            bodyImage.color = new Color(0.05f, 0.05f, 0.07f, 0.9f);
            bodyImage.raycastTarget = false;

            s_scrollRect = body.AddComponent<ScrollRect>();
            s_scrollRect.horizontal = false;
            s_scrollRect.movementType = ScrollRect.MovementType.Clamped;
            s_scrollRect.inertia = true;
            s_scrollRect.scrollSensitivity = 25f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(body.transform, worldPositionStays: false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -4f);

            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            s_content = content.GetComponent<RectTransform>();
            s_content.SetParent(viewport.transform, worldPositionStays: false);
            s_content.anchorMin = new Vector2(0f, 1f);
            s_content.anchorMax = new Vector2(1f, 1f);
            s_content.pivot = new Vector2(0.5f, 1f);
            s_content.offsetMin = Vector2.zero;
            s_content.offsetMax = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            s_scrollRect.viewport = viewportRect;
            s_scrollRect.content = s_content;
        }
    }
}
