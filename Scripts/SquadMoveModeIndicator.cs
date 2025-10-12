// =============================================================================
// Displays an on-counter "MOVE" label while a unit is in Move mode.
// =============================================================================

using System;
using UnityEngine;

namespace SquadOfSteelMod
{
    [RequireComponent(typeof(UnitGO))]
    public class SquadMoveModeIndicator : MonoBehaviour
    {
        const float LabelYOffset = 3.2f;

        UnitGO _owner;
        SpriteRenderer _truckRenderer;
        static Texture2D s_truckTexture;
        static readonly byte[] s_truckIconBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAEAAAAAgCAYAAACinX6EAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAH6SURBVGhD7ZfNSwJRFMVd9ie08L3bh0UEfRCVIIEVtCijchGtkhDKItStVETiIqJcBxEMtGkhLWzTzpa1CcKW2SKqRR/QKgShJm4xMnMnmhzHxuId+K3mvXvOOz4HdTiEhISEhIQqprHNjNufzESsAudRj6oWhh7fyuSsAudRj6qWKIAUMCed3a6mc49G4Dp6+H9RAB5u/yJfMALX0cOLAkQBooDfK6Avuufui0qRchlcSiWH44cHCvO7p8fx9OW5EbhOvU8B51GPT/as/X3wMTQi5f4MUcnamyEKEAWIAkQBOpNqRhRgYwHumY3r7qnlu57pxA19Vkk0vnYU0DG6cA8NTa+cc1kB6urf2oYCT3StlXzlywEKjLEYPYdpGRXQ3OV90QQgNLb15j2LO1d0X7kY+TLGTgCghp6nZH1XQPvI7IPa1OPxyD6fT/Z6vZowrQOTz3RvOfzUl3O+Ts9Tsr77L4DXTTELh8NyNpstkkgkNGHcgfUVut8spfgCQC09kyUCgE71J6AOoeD3+4tBnE7nBJ1hRnb56sQYm1dMQqGQLgQSi8WKQRhja3SGGdnlqxMA9Csm+P2jIZBgMKgOMkNnmJFdvjrhG1YxQSRJ0oRIpVKyy+UqPgeAFjrDjOzy/VKc8211GLySeP3wxaQOwTk/onvLUSV83wFh/HJWGEnCYwAAAABJRU5ErkJggg==");

        void Awake()
        {
            _owner = GetComponent<UnitGO>();
            CreateTruckIcon();
            HideImmediate();
        }

        public void Show()
        {
            if (_truckRenderer == null)
                CreateTruckIcon();

            if (_truckRenderer != null && !_truckRenderer.gameObject.activeSelf)
                _truckRenderer.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_truckRenderer != null && _truckRenderer.gameObject.activeSelf)
                _truckRenderer.gameObject.SetActive(false);
        }

        void HideImmediate()
        {
            if (_truckRenderer != null)
                _truckRenderer.gameObject.SetActive(false);
        }

        public static SquadMoveModeIndicator For(UnitGO owner)
        {
            if (owner == null)
                return null;

            var indicator = owner.GetComponent<SquadMoveModeIndicator>();
            if (indicator == null)
            {
                indicator = owner.gameObject.AddComponent<SquadMoveModeIndicator>();
            }
            return indicator;
        }

        void LateUpdate()
        {
            if (_truckRenderer != null)
            {
                _truckRenderer.transform.rotation = Quaternion.identity;
            }
        }

        void CreateTruckIcon()
        {
            if (_truckRenderer != null)
                return;

            var root = new GameObject("SquadOfSteel.MoveModeTruck");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, LabelYOffset, -0.05f);

            var spriteGO = new GameObject("TruckIcon");
            spriteGO.transform.SetParent(root.transform, false);
            _truckRenderer = spriteGO.AddComponent<SpriteRenderer>();

            var texture = GetOrCreateTruckTexture();
            if (texture == null)
            {
                Debug.LogWarning("[SquadOfSteel] Move mode truck texture missing.");
                return;
            }

            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 32f);
            _truckRenderer.sprite = sprite;
            _truckRenderer.color = Color.white;
            _truckRenderer.transform.localScale = new Vector3(1.4f, 1.4f, 1f);

            if (_owner != null && _owner.unitSprite != null)
            {
                _truckRenderer.sortingLayerID = _owner.unitSprite.sortingLayerID;
                _truckRenderer.sortingOrder = _owner.unitSprite.sortingOrder + 80;
            }
        }

        Texture2D GetOrCreateTruckTexture()
        {
            if (s_truckTexture != null)
                return s_truckTexture;

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (!texture.LoadImage(s_truckIconBytes))
                    return null;

                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                s_truckTexture = texture;
                return s_truckTexture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to decode truck icon texture: {ex.Message}");
                return null;
            }
        }
    }
}
