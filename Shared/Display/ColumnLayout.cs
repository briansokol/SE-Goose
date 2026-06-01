namespace IngameScript
{
    /// <summary>Pure column/row geometry. Resolves aligned text X for proportional fonts and stacks row Y positions.</summary>
    public static class ColumnLayout
    {
        /// <summary>Left-anchored draw X for text of a known measured width placed at a column reference X with the given alignment.</summary>
        /// <param name="columnX">Column reference X (left edge for Left, right edge for Right, center for Center).</param>
        /// <param name="measuredWidth">Pixel width of the text.</param>
        /// <param name="align">Desired alignment.</param>
        public static float ResolveTextX(float columnX, float measuredWidth, DrawAlign align)
        {
            if (align == DrawAlign.Right)
            {
                return columnX - measuredWidth;
            }
            if (align == DrawAlign.Center)
            {
                return columnX - measuredWidth / 2f;
            }
            return columnX;
        }

        /// <summary>Top Y of row <paramref name="index"/> when stacking fixed-height rows from a starting Y.</summary>
        public static float RowY(float startY, float rowHeight, int index)
        {
            return startY + rowHeight * index;
        }
    }
}
