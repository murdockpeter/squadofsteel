// =============================================================================
// Renders a thin scrolling debug overlay for combat events.
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

        public static void Initialize()
        {
            if (s_root != null)
                return;

            if (UIManager.instance == null || UIManager.instance.mainCanvas == null)
                return;

            EnsureFont();
            CreateOverlay();
            FlushPending();
            SetVisible(SquadCombatRuntime.DebugEnabled);
        }

        public static void SetVisible(bool visible)
        {
            if (!visible)
            {
                if (s_root != null)
                {
                    s_root.SetActive(false);
                    Debug.Log("[SquadOfSteel][Overlay] Hidden (debug off).");
                }
                return;
            }

            Initialize();

            if (s_root != null)
            {
                s_root.SetActive(true);
                if (s_entries.Count == 0)
                {
                    AddEntry("Combat debug overlay enabled.");
                }
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
            entryRect.offsetMin = new Vector2(0f, 0f);
            entryRect.offsetMax = new Vector2(0f, 0f);

            var text = entryGO.AddComponent<TextMeshProUGUI>();
            text.font = s_defaultFont;
            text.fontSize = 20;
            text.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.richText = false;
            text.raycastTarget = false;
            text.margin = new Vector4(4f, 2f, 4f, 6f);
            text.text = message.Replace("\r\n", "\n");
            Debug.Log($"[SquadOfSteel][Overlay] Entry text using font '{text.font?.name ?? "<null>"}'.");

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
            {
                s_scrollRect.verticalNormalizedPosition = 1f;
            }

            Debug.Log($"[SquadOfSteel][Overlay] Added entry (total {s_entries.Count}).");
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
            {
                AddEntry(entry);
            }
            s_pendingEntries.Clear();
        }

        static void EnsureFont()
        {
            if (s_defaultFont != null)
                return;

            s_defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (s_defaultFont == null && UIManager.instance != null && UIManager.instance.playerMoneyAmount_Text != null)
            {
                s_defaultFont = UIManager.instance.playerMoneyAmount_Text.font;
            }
            if (s_defaultFont == null)
            {
                s_defaultFont = TMP_Settings.defaultFontAsset;
            }
            if (s_defaultFont != null)
            {
                Debug.Log($"[SquadOfSteel][Overlay] Using font asset '{s_defaultFont.name}'.");
            }
            else
            {
                Debug.LogWarning("[SquadOfSteel][Overlay] No TMP font asset available; overlay text may not render.");
            }
        }

        static void CreateOverlay()
        {
            var canvas = UIManager.instance.mainCanvas;
            s_root = new GameObject("SquadOfSteelDebugOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var rect = s_root.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(20f, 40f);
            rect.offsetMax = new Vector2(320f, -40f);
            s_root.layer = canvas.gameObject.layer;
            s_root.transform.SetAsLastSibling();

            var background = s_root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);

            s_scrollRect = s_root.GetComponent<ScrollRect>();
            s_scrollRect.horizontal = false;
            s_scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(s_root.transform, worldPositionStays: false);
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(6f, 6f);
            viewportRect.offsetMax = new Vector2(-6f, -6f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            s_content = content.GetComponent<RectTransform>();
            s_content.SetParent(viewport.transform, worldPositionStays: false);
            s_content.anchorMin = new Vector2(0f, 1f);
            s_content.anchorMax = new Vector2(1f, 1f);
            s_content.pivot = new Vector2(0.5f, 1f);
            s_content.offsetMin = new Vector2(0f, 0f);
            s_content.offsetMax = new Vector2(0f, 0f);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            s_scrollRect.viewport = viewportRect;
            s_scrollRect.content = s_content;
        }
    }
}
