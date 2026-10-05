using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>
    /// A mesh drawn over one of the map images, parented under that map's pin root. Shared by the
    /// portal lines and the route so <see cref="MapOverlay"/> can build and restyle both alike.
    /// </summary>
    public abstract class MapGraphic : MaskableGraphic
    {
        public RawImage MapImage;
        public bool IsLarge = true;

        /// <summary>A setting that changes how this draws was edited; rebuild on the next frame.</summary>
        protected bool StyleDirty = true;

        public void MarkStyleDirty()
        {
            StyleDirty = true;
        }
    }
}
