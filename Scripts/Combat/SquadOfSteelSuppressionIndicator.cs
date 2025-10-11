// =============================================================================
// Displays suppression intensity to the right of a unit counter with a badge.
// =============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    [RequireComponent(typeof(UnitGO))]
    public class SquadOfSteelSuppressionIndicator : MonoBehaviour
    {
        static readonly HashSet<SquadOfSteelSuppressionIndicator> s_instances = new HashSet<SquadOfSteelSuppressionIndicator>();

        const float LabelYOffset = 1.2f;
        const float LabelXOffset = 3.8f;
        const float BadgeRadius = 0.75f;
        const float BadgeWidth = 0.12f;
        const int BadgeSegments = 36;

        UnitGO _owner;
        TextMeshPro _label;
        LineRenderer _ringOutline;
        LineRenderer _ring;
        int _lastSuppression = int.MinValue;
        Color _baseTextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        readonly Color _textHighlightColor = new Color(1f, 0.45f, 0.15f, 1f);
        readonly Color _ringLowColor = new Color(0.35f, 0.85f, 0.35f, 0.9f);
        readonly Color _ringHighColor = new Color(0.95f, 0.25f, 0.15f, 0.95f);
        static Material s_ringMaterial;

        void Awake()
        {
            _owner = GetComponent<UnitGO>();
            CreateBadge();
            s_instances.Add(this);
        }

        void OnDestroy()
        {
            s_instances.Remove(this);
            if (_label != null)
            {
                Destroy(_label.gameObject);
                _label = null;
            }
            if (_ring != null)
            {
                Destroy(_ring.gameObject);
                _ring = null;
            }
            if (_ringOutline != null)
            {
                Destroy(_ringOutline.gameObject);
                _ringOutline = null;
            }
        }

        void CreateBadge()
        {
            if (_label != null || _owner == null)
                return;

            var badgeRoot = new GameObject("SquadOfSteel.SuppressionBadge");
            badgeRoot.transform.SetParent(transform, false);
            badgeRoot.transform.localPosition = new Vector3(LabelXOffset, LabelYOffset, -0.1f);

            var labelGO = new GameObject("Value");
            labelGO.transform.SetParent(badgeRoot.transform, false);
            _label = labelGO.AddComponent<TextMeshPro>();
            _label.enableAutoSizing = false;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.richText = false;
            _label.text = string.Empty;
            _label.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            _label.margin = new Vector4(0f, 0f, 0f, 0f);
            _label.transform.localScale = Vector3.one;
            _label.enableWordWrapping = false;

            var labelRenderer = _label.GetComponent<MeshRenderer>();
            ApplyTemplateStyle();

            if (labelRenderer != null && _owner.unitSprite != null)
            {
                labelRenderer.sortingLayerID = _owner.unitSprite.sortingLayerID;
                labelRenderer.sortingOrder = _owner.unitSprite.sortingOrder + 60;
            }

            if (s_ringMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    s_ringMaterial = new Material(shader)
                    {
                        name = "SquadOfSteel_SuppressionRing"
                    };
                }
            }

            _ringOutline = CreateRingRenderer(badgeRoot.transform, "RingOutline");
            _ring = CreateRingRenderer(badgeRoot.transform, "Ring");

            if (_ring != null && _owner.unitSprite != null)
            {
                _ring.sortingLayerID = _owner.unitSprite.sortingLayerID;
                _ring.sortingOrder = _owner.unitSprite.sortingOrder + 55;
            }
            if (_ringOutline != null && _owner.unitSprite != null)
            {
                _ringOutline.sortingLayerID = _owner.unitSprite.sortingLayerID;
                _ringOutline.sortingOrder = _owner.unitSprite.sortingOrder + 54;
            }

            if (_ringOutline != null)
            {
                _ringOutline.startColor = Color.black;
                _ringOutline.endColor = Color.black;
            }
        }

        void ApplyTemplateStyle()
        {
            if (_label == null)
                return;

            var template = FindTemplateLabel();
            if (template != null)
            {
                _label.font = template.font;
                _label.fontSize = template.fontSize;
                _label.fontStyle = template.fontStyle;
                _label.enableAutoSizing = template.enableAutoSizing;
                _label.alignment = template.alignment;
                _label.color = template.color;
                _baseTextColor = template.color;

                if (template.fontSharedMaterial != null)
                {
                    _label.fontMaterial = new Material(template.fontSharedMaterial)
                    {
                        name = "SquadOfSteel_SuppressionFontMat"
                    };
                }
                return;
            }

            // Fallback styling roughly matching stock counter numbers.
            if (_label.font == null)
                _label.font = TMP_Settings.defaultFontAsset;

            _label.fontSize = 4.2f;
            _label.fontStyle = FontStyles.Bold;
            _label.enableAutoSizing = false;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Color.white;
            _baseTextColor = Color.white;

            var sharedMat = _label.fontSharedMaterial;
            if (sharedMat != null)
            {
                var outlineMat = new Material(sharedMat)
                {
                    name = "SquadOfSteel_SuppressionFallbackMat"
                };
                outlineMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.4f);
                outlineMat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                _label.fontMaterial = outlineMat;
            }
        }

        TextMeshPro FindTemplateLabel()
        {
            if (_owner == null)
                return null;

            var labels = _owner.GetComponentsInChildren<TextMeshPro>(true);
            foreach (var candidate in labels)
            {
                if (candidate == null)
                    continue;

                string name = candidate.name ?? string.Empty;
                if (name.IndexOf("Suppression", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                // Prefer labels that appear to render numeric stats.
                if (candidate.text != null && candidate.text.Trim().Length <= 3)
                    return candidate;
            }

            return null;
        }

        LineRenderer CreateRingRenderer(Transform parent, string name)
        {
            var ringGO = new GameObject(name);
            ringGO.transform.SetParent(parent, false);
            var renderer = ringGO.AddComponent<LineRenderer>();
            renderer.loop = true;
            renderer.positionCount = BadgeSegments;
            renderer.useWorldSpace = false;
            renderer.widthMultiplier = BadgeWidth;
            renderer.numCornerVertices = 0;
            renderer.numCapVertices = 0;
            renderer.textureMode = LineTextureMode.Stretch;
            renderer.alignment = LineAlignment.TransformZ;
            if (s_ringMaterial != null)
                renderer.material = s_ringMaterial;

            for (int i = 0; i < BadgeSegments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / BadgeSegments;
                float x = Mathf.Cos(angle) * BadgeRadius;
                float y = Mathf.Sin(angle) * BadgeRadius;
                renderer.SetPosition(i, new Vector3(x, y, 0f));
            }

            return renderer;
        }

        void LateUpdate()
        {
            if (_label == null)
                return;

            _label.transform.rotation = Quaternion.identity;
            if (_ring != null)
                _ring.transform.rotation = Quaternion.identity;
            if (_ringOutline != null)
                _ringOutline.transform.rotation = Quaternion.identity;
        }

        public void Refresh()
        {
            if (_owner?.unit == null)
                return;

            CreateBadge();
            if (_label == null || _ring == null)
                return;

            int suppression = SquadOfSteelSuppression.Get(_owner.unit);
            if (suppression == _lastSuppression)
                return;

            _lastSuppression = suppression;

            if (suppression <= 0)
            {
                _label.text = string.Empty;
                if (_label.gameObject.activeSelf)
                    _label.gameObject.SetActive(false);
                if (_ring.gameObject.activeSelf)
                    _ring.gameObject.SetActive(false);
                if (_ringOutline != null && _ringOutline.gameObject.activeSelf)
                    _ringOutline.gameObject.SetActive(false);
                return;
            }

            if (!_label.gameObject.activeSelf)
                _label.gameObject.SetActive(true);
            if (!_ring.gameObject.activeSelf)
                _ring.gameObject.SetActive(true);
            if (_ringOutline != null && !_ringOutline.gameObject.activeSelf)
                _ringOutline.gameObject.SetActive(true);

            _label.text = suppression.ToString();

            float t = Mathf.Clamp01(suppression / 100f);
            _label.color = Color.Lerp(_baseTextColor, _textHighlightColor, t);

            Color ringColor = Color.Lerp(_ringLowColor, _ringHighColor, t);
            _ring.startColor = ringColor;
            _ring.endColor = ringColor;
        }

        public static SquadOfSteelSuppressionIndicator For(UnitGO owner)
        {
            if (owner == null)
                return null;

            var indicator = owner.GetComponent<SquadOfSteelSuppressionIndicator>();
            if (indicator == null)
            {
                indicator = owner.gameObject.AddComponent<SquadOfSteelSuppressionIndicator>();
            }
            return indicator;
        }

        public static void RefreshAll()
        {
            foreach (var indicator in s_instances)
            {
                if (indicator == null)
                    continue;

                indicator._lastSuppression = int.MinValue;
                indicator.Refresh();
            }
        }
    }
}
