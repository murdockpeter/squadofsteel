// =============================================================================
// Displays suppression intensity above a unit counter.
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    [RequireComponent(typeof(UnitGO))]
    public class SquadOfSteelSuppressionIndicator : MonoBehaviour
    {
        static readonly HashSet<SquadOfSteelSuppressionIndicator> s_instances = new HashSet<SquadOfSteelSuppressionIndicator>();

        const float LabelYOffset = 1.4f;
        const float LabelXOffset = 3.2f;

        UnitGO _owner;
        TextMeshPro _label;
        int _lastSuppression = int.MinValue;

        void Awake()
        {
            _owner = GetComponent<UnitGO>();
            CreateLabel();
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
        }

        void CreateLabel()
        {
            if (_label != null || _owner == null)
                return;

            var labelGO = new GameObject("SquadOfSteel.SuppressionLabel");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(LabelXOffset, LabelYOffset, -0.1f);

            _label = labelGO.AddComponent<TextMeshPro>();
            _label.fontSize = 2.6f;
            _label.enableAutoSizing = false;
            _label.alignment = TextAlignmentOptions.MidlineLeft;
            _label.richText = false;
            _label.text = string.Empty;
            _label.color = new Color(0f, 0.75f, 0f, 0.95f);
            _label.margin = new Vector4(0f, 0f, 0f, 0f);
            _label.transform.localScale = Vector3.one;

            var renderer = _label.GetComponent<MeshRenderer>();
            if (renderer != null && _owner.unitSprite != null)
            {
                renderer.sortingLayerID = _owner.unitSprite.sortingLayerID;
                renderer.sortingOrder = _owner.unitSprite.sortingOrder + 50;
            }

            Debug.Log($"[SquadOfSteel][Indicator] Created for '{_owner.unit?.Name ?? "<null>"}'.");
        }

        void LateUpdate()
        {
            if (_label == null)
                return;

            // Keep label upright towards camera (2D top-down).
            _label.transform.rotation = Quaternion.identity;
        }

        public void Refresh()
        {
            if (_owner?.unit == null)
                return;

            CreateLabel();
            if (_label == null)
                return;

            int suppression = SquadOfSteelSuppression.Get(_owner.unit);
            if (suppression == _lastSuppression)
                return;

            _lastSuppression = suppression;

            if (suppression <= 0)
            {
                _label.text = string.Empty;
                _label.gameObject.SetActive(false);
                Debug.Log($"[SquadOfSteel][Indicator] {_owner.unit.Name}: cleared (suppression 0).");
                return;
            }

            _label.gameObject.SetActive(true);
            _label.text = suppression.ToString();

            float t = Mathf.Clamp01(suppression / 100f);
            Color color = Color.Lerp(new Color(0.2f, 0.8f, 0.2f, 0.95f), new Color(0.9f, 0.15f, 0.05f, 0.98f), t);
            _label.color = color;
            Debug.Log($"[SquadOfSteel][Indicator] {_owner.unit.Name}: suppression {suppression}, color {color}.");
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
