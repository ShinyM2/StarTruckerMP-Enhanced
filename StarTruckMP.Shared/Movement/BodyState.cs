namespace StarTruckMP.Shared.Movement
{
    /// <summary>Where one moving thing is and how it moves: a cab or a trailer, in world space.</summary>
    public struct BodyState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngVel;
    }

    /// <summary>
    /// One moment of a player's motion: the cab and, when one is hitched, the trailer, read in
    /// the same physics step and stamped with the same clock. A movement packet carries the
    /// current one and repeats the few before it.
    /// </summary>
    public struct MovementEntry
    {
        /// <summary>The sender's per-stream counter; consecutive packets have consecutive values.</summary>
        public uint Seq;

        /// <summary>The sender's own clock at the reading, in milliseconds. Only differences mean anything.</summary>
        public long SentAt;

        public bool HasCab;
        public BodyState Cab;

        public bool HasTrailer;
        public BodyState Trailer;
    }
}
