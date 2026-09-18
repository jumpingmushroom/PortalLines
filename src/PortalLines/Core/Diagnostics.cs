using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.Core
{
    /// <summary>
    /// One-shot report to the BepInEx log the first time the large map opens in a world.
    ///
    /// The overlay's geometry rests on assumptions about the map's UI hierarchy that cannot be
    /// checked by reading the assembly: that the pin root's rect matches the map image's, that
    /// pins are anchored at its bottom-left, and whether a mask clips it. Logging them means a
    /// misplaced line can be diagnosed from a log file rather than from a screenshot.
    /// </summary>
    public static class Diagnostics
    {
        private static bool _reported;

        public static void Reset()
        {
            _reported = false;
        }

        public static void ReportOnce(Minimap map)
        {
            if (_reported || map == null)
                return;
            _reported = true;

            var sb = new StringBuilder(512);
            sb.Append("map diagnostics: ");

            RectTransform img = map.m_mapImageLarge != null ? map.m_mapImageLarge.rectTransform : null;
            RectTransform root = map.m_pinRootLarge;
            if (img != null)
                sb.Append("imageRect=").Append(img.rect).Append(' ');
            if (root != null)
            {
                sb.Append("pinRootRect=").Append(root.rect)
                  .Append(" pinRootAnchors=").Append(root.anchorMin).Append('/').Append(root.anchorMax)
                  .Append(" pinRootPivot=").Append(root.pivot)
                  .Append(" mask=").Append(root.GetComponentInParent<Mask>() != null)
                  .Append(" rectMask=").Append(root.GetComponentInParent<RectMask2D>() != null)
                  .Append(' ');

                // The first real pin tells us the anchoring convention pins actually use.
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i) as RectTransform;
                    if (child == null || child.name.StartsWith("PortalLines"))
                        continue;
                    sb.Append("samplePin=").Append(child.name)
                      .Append(" anchors=").Append(child.anchorMin).Append('/').Append(child.anchorMax)
                      .Append(" pivot=").Append(child.pivot).Append(' ');
                    break;
                }
            }

            sb.Append("textureSize=").Append(map.m_textureSize)
              .Append(" pixelSize=").Append(map.m_pixelSize).Append(' ');

            Game game = Game.instance;
            if (game != null && game.m_portalPrefabs != null)
            {
                sb.Append("portalPrefabs=[");
                List<GameObject> prefabs = game.m_portalPrefabs;
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(prefabs[i] != null ? prefabs[i].name : "null");
                }
                sb.Append("] ");
            }

            ZNet net = ZNet.instance;
            if (net != null)
            {
                sb.Append("server=").Append(net.IsServer())
                  .Append(" simDistance=").Append(net.GetSyncedSimulationDistance().NearSimulationDistance)
                  .Append(' ');
            }

            sb.Append("known=").Append(PortalRegistry.Snapshot.Portals.Count)
              .Append(" links=").Append(PortalRegistry.Snapshot.Links.Count)
              .Append(" pending=").Append(PortalRegistry.Snapshot.PendingPartners);

            PortalLinesPlugin.Log.LogInfo(sb.ToString());
        }
    }
}
