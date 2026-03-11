namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// User-customisable inactive sketch colour palette.
    /// Stored in settings as RGB values so it serialises cleanly.
    /// </summary>
    public sealed class InactiveSketchPaletteSettings
    {
        public Rgb24 Projected { get; set; }

        public Rgb24 UnderCurve { get; set; }
        public Rgb24 FullCurve { get; set; }
        public Rgb24 OverCurve { get; set; }

        public Rgb24 UnderConstruction { get; set; }
        public Rgb24 FullConstruction { get; set; }
        public Rgb24 OverConstruction { get; set; }

        public static InactiveSketchPaletteSettings CreateDefaults()
        {
            // Matches the previous hard-coded palette in SketchStyler.EnsureInactivePalette().
            return new InactiveSketchPaletteSettings
            {
                Projected = new Rgb24(150, 130, 80),

                UnderCurve = new Rgb24(35, 70, 140),
                FullCurve = new Rgb24(150, 150, 150),
                OverCurve = new Rgb24(180, 60, 60),

                UnderConstruction = new Rgb24(70, 130, 140),
                FullConstruction = new Rgb24(110, 110, 110),
                OverConstruction = new Rgb24(160, 80, 80),
            };
        }

        public void SanitizeInPlace()
        {
            Projected = Projected.Clamp();
            UnderCurve = UnderCurve.Clamp();
            FullCurve = FullCurve.Clamp();
            OverCurve = OverCurve.Clamp();
            UnderConstruction = UnderConstruction.Clamp();
            FullConstruction = FullConstruction.Clamp();
            OverConstruction = OverConstruction.Clamp();
        }
    }
}
