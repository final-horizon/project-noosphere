
namespace Content.Shared._Carmine.OccluderSmooth
{
    /// <summary>
    /// This component marks a wall or another entity to modify it's occluder component when nearby entities with occludersmooth components are present.
    /// This is used to achieve an effect similar to IconSmoothSystem but for occluders pretty much.
    /// </remarks>
    [RegisterComponent]
    public sealed partial class OccluderSmoothComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite), DataField("enabled")]
        public bool Enabled = true;

        public (EntityUid?, Vector2i)? LastPosition;

        /// <summary>
        ///     Used by <see cref="OccluderSmoothSystem"/> to reduce redundant updates.
        /// </summary>
        public int UpdateGeneration { get; set; }

        [ViewVariables(VVAccess.ReadWrite)]
        public WallConnections Connections = 0;

        [ViewVariables(VVAccess.ReadWrite)]
        public Angle Rotation = Angle.Zero;

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? OccluderAlpha;

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? OccluderBeta;
        /// <summary>
        /// used for windows
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite), DataField("transparent")]
        public bool Transparent = false;

        public enum WallConnections : byte
        {
            None = 0,
            North = 1 << 0,  // 0000_0001
            East = 1 << 2,  // 0000_0100
            South = 1 << 4,  // 0001_0000
            West = 1 << 6,  // 0100_0000
        }
    }
}
