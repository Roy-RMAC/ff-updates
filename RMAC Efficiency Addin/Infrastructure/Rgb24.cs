namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Small, JSON-friendly RGB container (0-255 per channel).
    /// </summary>
    public readonly record struct Rgb24(int R, int G, int B)
    {
        public Rgb24 Clamp()
            => new(System.Math.Clamp(R, 0, 255),
                   System.Math.Clamp(G, 0, 255),
                   System.Math.Clamp(B, 0, 255));
    }
}
