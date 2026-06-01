namespace IngameScript
{
    /// <summary>Measurement seam. Supplies text pixel size so column layout and vertical scaling work without a live surface (tests inject fakes; runtime uses <see cref="SurfaceMeasurer"/>).</summary>
    public interface IStringMeasurer
    {
        /// <summary>Pixel width and height of <paramref name="text"/> rendered with the given font and scale.</summary>
        MeasuredSize Measure(string text, string font, float scale);
    }
}
