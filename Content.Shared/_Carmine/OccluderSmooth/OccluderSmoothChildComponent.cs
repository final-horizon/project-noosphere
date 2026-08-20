
namespace Content.Shared._Carmine.OccluderSmooth
{
    /// <summary>
    /// This component is purely a marker for child entities spawned by OccluderSmoothComponent.
    /// </remarks>
    [RegisterComponent]
    public sealed partial class OccluderSmoothChildComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite), DataField("enabled")]
        public bool Enabled = true;
    }
}
