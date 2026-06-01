using FluentAssertions;
using IngameScript;
using VRageMath;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Deterministic measurer for layout tests: width scales with character count, height is fixed.</summary>
    internal sealed class FakeMeasurer : IStringMeasurer
    {
        private readonly float _charWidth;
        private readonly float _lineHeight;

        public FakeMeasurer(float charWidth, float lineHeight)
        {
            _charWidth = charWidth;
            _lineHeight = lineHeight;
        }

        public MeasuredSize Measure(string text, string font, float scale)
        {
            int len = text == null ? 0 : text.Length;
            return new MeasuredSize(len * _charWidth, _lineHeight);
        }
    }

    /// <summary>Tests for the pure Display layout layer.</summary>
    public class DisplayTests
    {
        private const float Tol = 0.01f;

        [Fact]
        public void Viewport_centers_texture_minus_surface_with_no_padding()
        {
            var vp = DisplayViewport.FromSurfaceMetrics(new Vector2(100, 80), new Vector2(120, 100), 0f);
            vp.Origin.X.Should().BeApproximately(10f, Tol);
            vp.Origin.Y.Should().BeApproximately(10f, Tol);
            vp.Size.X.Should().BeApproximately(100f, Tol);
            vp.Size.Y.Should().BeApproximately(80f, Tol);
        }

        [Fact]
        public void Viewport_applies_padding_on_both_edges()
        {
            var vp = DisplayViewport.FromSurfaceMetrics(new Vector2(100, 80), new Vector2(120, 100), 10f);
            vp.Origin.X.Should().BeApproximately(20f, Tol);
            vp.Origin.Y.Should().BeApproximately(18f, Tol);
            vp.Size.X.Should().BeApproximately(80f, Tol);
            vp.Size.Y.Should().BeApproximately(64f, Tol);
        }

        [Fact]
        public void Viewport_equal_texture_and_surface_has_zero_origin()
        {
            var vp = DisplayViewport.FromSurfaceMetrics(new Vector2(512, 512), new Vector2(512, 512), 0f);
            vp.Origin.X.Should().BeApproximately(0f, Tol);
            vp.Origin.Y.Should().BeApproximately(0f, Tol);
            vp.Size.X.Should().BeApproximately(512f, Tol);
            vp.Size.Y.Should().BeApproximately(512f, Tol);
        }

        [Fact]
        public void ResolveTextX_left_returns_column()
        {
            ColumnLayout.ResolveTextX(50f, 20f, DrawAlign.Left).Should().BeApproximately(50f, Tol);
        }

        [Fact]
        public void ResolveTextX_right_subtracts_width()
        {
            ColumnLayout.ResolveTextX(200f, 30f, DrawAlign.Right).Should().BeApproximately(170f, Tol);
        }

        [Fact]
        public void ResolveTextX_center_subtracts_half_width()
        {
            ColumnLayout.ResolveTextX(100f, 40f, DrawAlign.Center).Should().BeApproximately(80f, Tol);
        }

        [Fact]
        public void RowY_stacks_by_height()
        {
            ColumnLayout.RowY(0f, 26f, 0).Should().BeApproximately(0f, Tol);
            ColumnLayout.RowY(0f, 26f, 2).Should().BeApproximately(52f, Tol);
            ColumnLayout.RowY(10f, 20f, 3).Should().BeApproximately(70f, Tol);
        }

        [Fact]
        public void FillWidth_is_proportional_and_clamped()
        {
            StatusBarGeometry.FillWidth(200f, 0).Should().BeApproximately(0f, Tol);
            StatusBarGeometry.FillWidth(200f, 50).Should().BeApproximately(100f, Tol);
            StatusBarGeometry.FillWidth(200f, 100).Should().BeApproximately(200f, Tol);
            StatusBarGeometry.FillWidth(200f, -10).Should().BeApproximately(0f, Tol);
            StatusBarGeometry.FillWidth(200f, 150).Should().BeApproximately(200f, Tol);
        }

        [Fact]
        public void FitScale_grows_up_to_max_when_space_is_ample()
        {
            LayoutScaler.FitScale(200f, 4, 20f, 1f, 0.3f, 1f).Should().BeApproximately(1f, Tol);
        }

        [Fact]
        public void FitScale_shrinks_to_fit_rows()
        {
            LayoutScaler.FitScale(40f, 4, 20f, 1f, 0.3f, 1f).Should().BeApproximately(0.5f, Tol);
        }

        [Fact]
        public void FitScale_clamps_to_min()
        {
            LayoutScaler.FitScale(20f, 4, 20f, 1f, 0.3f, 1f).Should().BeApproximately(0.3f, Tol);
        }

        [Fact]
        public void FitScale_respects_line_spacing()
        {
            LayoutScaler.FitScale(100f, 4, 20f, 1.25f, 0.3f, 1f).Should().BeApproximately(1f, Tol);
        }

        [Fact]
        public void RowHeight_multiplies_line_height_scale_and_spacing()
        {
            LayoutScaler.RowHeight(20f, 0.5f, 1.25f).Should().BeApproximately(12.5f, Tol);
        }

        private static DisplayViewport Viewport(float ox, float oy, float sx, float sy)
        {
            var vp = new DisplayViewport();
            vp.Origin = new Vector2(ox, oy);
            vp.Size = new Vector2(sx, sy);
            return vp;
        }

        [Fact]
        public void AddText_left_offsets_by_origin()
        {
            var b = new DisplayFrameBuilder(Viewport(100, 50, 400, 300), new FakeMeasurer(10f, 20f));
            b.AddText("AB", 10f, 5f, DrawAlign.Left, "Debug", 0.5f, Color.White);

            b.Commands.Count.Should().Be(1);
            DrawCommand c = b.Commands[0];
            c.Kind.Should().Be(DrawKind.Text);
            c.Text.Should().Be("AB");
            c.X.Should().BeApproximately(110f, Tol);
            c.Y.Should().BeApproximately(55f, Tol);
            c.Scale.Should().BeApproximately(0.5f, Tol);
            c.Align.Should().Be(DrawAlign.Left);
        }

        [Fact]
        public void AddText_right_resolves_then_offsets_by_origin()
        {
            var b = new DisplayFrameBuilder(Viewport(100, 50, 400, 300), new FakeMeasurer(10f, 20f));
            b.AddText("ABC", 200f, 0f, DrawAlign.Right, "Debug", 1f, Color.White);

            b.Commands[0].X.Should().BeApproximately(270f, Tol);
        }

        [Fact]
        public void AddRect_offsets_by_origin()
        {
            var b = new DisplayFrameBuilder(Viewport(100, 50, 400, 300), new FakeMeasurer(10f, 20f));
            b.AddRect(10f, 20f, 50f, 8f, Color.White);

            DrawCommand c = b.Commands[0];
            c.Kind.Should().Be(DrawKind.Rect);
            c.X.Should().BeApproximately(110f, Tol);
            c.Y.Should().BeApproximately(70f, Tol);
            c.Width.Should().BeApproximately(50f, Tol);
            c.Height.Should().BeApproximately(8f, Tol);
        }

        [Fact]
        public void AddBar_emits_track_and_fill_with_proportional_width()
        {
            var track = new Color(40, 40, 40);
            var fill = new Color(60, 160, 220);
            var b = new DisplayFrameBuilder(Viewport(100, 50, 400, 300), new FakeMeasurer(10f, 20f));
            b.AddBar(0f, 0f, 100f, 10f, 50, track, fill);

            b.Commands.Count.Should().Be(2);
            b.Commands[0].Width.Should().BeApproximately(100f, Tol);
            b.Commands[0].Color.Should().Be(track);
            b.Commands[1].Width.Should().BeApproximately(50f, Tol);
            b.Commands[1].Color.Should().Be(fill);
        }

        [Fact]
        public void AddBar_omits_fill_at_zero_percent()
        {
            var b = new DisplayFrameBuilder(Viewport(100, 50, 400, 300), new FakeMeasurer(10f, 20f));
            b.AddBar(0f, 0f, 100f, 10f, 0, Color.Gray, Color.Green);

            b.Commands.Count.Should().Be(1);
        }

        [Fact]
        public void Reset_clears_commands()
        {
            var b = new DisplayFrameBuilder(Viewport(0, 0, 100, 100), new FakeMeasurer(10f, 20f));
            b.AddRect(0f, 0f, 10f, 10f, Color.White);
            b.Reset();
            b.Commands.Count.Should().Be(0);
        }
    }
}
