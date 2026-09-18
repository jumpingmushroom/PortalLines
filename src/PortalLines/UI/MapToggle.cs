using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// A "Portal lines" checkbox on the large map, cloned from the game's own "Visible to other
    /// players" toggle so it matches the map's style, and placed one row above whatever toggles
    /// already sit in that column (the cartography-table toggle lives there too).
    /// </summary>
    internal sealed class MapToggle
    {
        private Toggle _toggle;
        private bool _syncing;

        public bool Created => _toggle != null;

        public void Create(Minimap map)
        {
            if (_toggle != null || map == null || map.m_publicPosition == null || map.m_largeRoot == null)
                return;

            Toggle template = map.m_publicPosition;
            var templateRt = template.transform as RectTransform;
            RectTransform parent = template.transform.parent as RectTransform;
            if (templateRt == null || parent == null)
                return;

            // Every toggle on the large map in the same column as the template, measured in the
            // template parent's local space so hierarchy differences do not matter.
            float templateX = parent.InverseTransformPoint(templateRt.position).x;
            float templateY = parent.InverseTransformPoint(templateRt.position).y;
            float halfWidth = Mathf.Max(40f, templateRt.rect.width * 0.5f);
            var ys = new List<float>();
            var sb = new StringBuilder("map toggles: ");
            foreach (Toggle t in map.m_largeRoot.GetComponentsInChildren<Toggle>(true))
            {
                Vector3 local = parent.InverseTransformPoint(t.transform.position);
                bool sameColumn = Mathf.Abs(local.x - templateX) <= halfWidth;
                sb.Append(t.name).Append('@').Append(local.y.ToString("0")).Append(sameColumn ? "* " : " ");
                if (sameColumn)
                    ys.Add(local.y);
            }

            float top = templateY;
            float bottom = templateY;
            for (int i = 0; i < ys.Count; i++)
            {
                if (ys[i] > top) top = ys[i];
                if (ys[i] < bottom) bottom = ys[i];
            }
            float spacing = ys.Count >= 2 ? (top - bottom) / (ys.Count - 1) : templateRt.rect.height + 6f;

            GameObject go = Object.Instantiate(template.gameObject, parent);
            go.name = "PortalLinesToggle";
            var rt = go.transform as RectTransform;
            rt.anchoredPosition = templateRt.anchoredPosition + new Vector2(0f, (top - templateY) + spacing);

            // The template's label re-localizes itself on language change; ours must not.
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Localize")
                    Object.Destroy(c);

            TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "Portal lines";

            _toggle = go.GetComponent<Toggle>();
            _toggle.onValueChanged = new Toggle.ToggleEvent(); // drops the prefab's persistent call
            _toggle.SetIsOnWithoutNotify(PluginConfig.LinesEnabled.Value);
            _toggle.onValueChanged.AddListener(OnToggled);

            PortalLinesPlugin.Log.LogInfo(sb.Append("-> placed at ").Append(rt.anchoredPosition.ToString("0")).ToString());
        }

        public void SetVisible(bool visible)
        {
            if (_toggle != null && _toggle.gameObject.activeSelf != visible)
                _toggle.gameObject.SetActive(visible);
        }

        /// <summary>Config changed elsewhere (hotkey, ConfigurationManager): mirror it.</summary>
        public void Sync()
        {
            if (_toggle == null || _syncing)
                return;
            _syncing = true;
            _toggle.SetIsOnWithoutNotify(PluginConfig.LinesEnabled.Value);
            _syncing = false;
        }

        public void Destroy()
        {
            if (_toggle != null)
                Object.Destroy(_toggle.gameObject);
            _toggle = null;
        }

        private void OnToggled(bool on)
        {
            if (_syncing)
                return;
            _syncing = true;
            PluginConfig.LinesEnabled.Value = on;
            _syncing = false;
        }
    }
}
