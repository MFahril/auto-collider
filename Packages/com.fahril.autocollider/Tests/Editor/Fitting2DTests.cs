using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M8: 2D fitters (box, circle, capsule) and the primitive picker.</summary>
    public class Fitting2DTests
    {
        private static float2[] Rectangle(float2 center, float2 size, float angleDegrees)
        {
            var r = math.radians(angleDegrees);
            var u = new float2(math.cos(r), math.sin(r)) * size.x * 0.5f;
            var v = new float2(-math.sin(r), math.cos(r)) * size.y * 0.5f;
            return new[] { center - u - v, center + u - v, center + u + v, center - u + v };
        }

        private static float2[] Polygon(float2 center, float radius, int sides)
        {
            var points = new float2[sides];
            for (var i = 0; i < sides; i++)
            {
                var a = 2f * math.PI * i / sides;
                points[i] = center + new float2(math.cos(a), math.sin(a)) * radius;
            }
            return points;
        }

        /// <summary>Stadium along Y (or X): radius r, total length h.</summary>
        private static float2[] Stadium(float r, float h, bool vertical)
        {
            // Counter-clockwise: top arc right → left, then bottom arc left → right.
            var points = new List<float2>();
            var half = h * 0.5f - r;
            for (var i = 0; i <= 16; i++)
            {
                var a = math.PI * i / 16;
                points.Add(new float2(math.cos(a) * r, half + math.sin(a) * r));
            }
            for (var i = 0; i <= 16; i++)
            {
                var a = math.PI + math.PI * i / 16;
                points.Add(new float2(math.cos(a) * r, -half + math.sin(a) * r));
            }
            return vertical ? points.ToArray() : points.Select(p => new float2(p.y, p.x)).ToArray();
        }

        private static Outline2D Outline(float2[] loop) => new("test", Polygon2D.OrientByNesting(new[] { loop }));

        [Test]
        public void Aabb_IsExact()
        {
            var box = Box2DFitter.Aabb(Rectangle(new float2(1, 2), new float2(2, 1), 0));

            Assert.That(box.Center, Is.EqualTo(new float2(1, 2)));
            Assert.That(box.Size.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(box.Size.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(box.Angle, Is.EqualTo(0f));
        }

        [Test]
        public void MinArea_RecoversRotation()
        {
            var points = Rectangle(new float2(3, -1), new float2(4, 1), 30f);
            var outline = Outline(points);

            var box = Box2DFitter.MinArea(outline.Hull, points);

            Assert.That(box.Area, Is.EqualTo(4f).Within(0.01f));
            Assert.That(box.Angle, Is.EqualTo(30f).Within(0.1f).Or.EqualTo(-60f).Within(0.1f));
            Assert.That(math.distance(box.Center, new float2(3, -1)), Is.LessThan(1e-3f));
        }

        [Test]
        public void Welzl_FindsTheCircle_Deterministically()
        {
            var points = Polygon(new float2(1, 2), 1.5f, 64);

            var a = CircleFitter.Welzl(points);
            var b = CircleFitter.Welzl(points);

            Assert.That(a.Radius, Is.EqualTo(1.5f).Within(0.015f));
            Assert.That(math.distance(a.Center, new float2(1, 2)), Is.LessThan(0.01f));
            Assert.That(b.Radius, Is.EqualTo(a.Radius));
            Assert.That(b.Center, Is.EqualTo(a.Center));
        }

        [Test]
        public void Capsule_FollowsTheLongAxis()
        {
            var vertical = Capsule2DFitter.Best(Stadium(0.5f, 3f, true), allowRotation: false);
            var horizontal = Capsule2DFitter.Best(Stadium(0.5f, 3f, false), allowRotation: false);

            Assert.That(vertical.Vertical, Is.True);
            Assert.That(vertical.Radius, Is.EqualTo(0.5f).Within(0.01f));
            // Cap slack (4 % of r per cap, for pixel stair corners) may shorten an exact capsule by up to 0.08 r.
            Assert.That(vertical.Height, Is.EqualTo(3f).Within(0.05f));
            Assert.That(horizontal.Vertical, Is.False);
            Assert.That(horizontal.Height, Is.EqualTo(3f).Within(0.05f));
        }

        [Test]
        public void Capsule_RotatedAxis_WhenAllowed()
        {
            var points = Stadium(0.25f, 2f, true);
            var r = math.radians(40f);
            var rotated = points.Select(p => new float2(p.x * math.cos(r) - p.y * math.sin(r), p.x * math.sin(r) + p.y * math.cos(r))).ToArray();

            var capsule = Capsule2DFitter.Best(rotated, allowRotation: true);

            Assert.That(capsule.Radius, Is.EqualTo(0.25f).Within(0.01f));
            Assert.That(capsule.Height, Is.EqualTo(2f).Within(0.03f));
            Assert.That(math.abs(capsule.Angle), Is.EqualTo(40f).Within(1f));
        }

        [Test]
        public void BestPrimitive_PicksTheMatchingShape()
        {
            var context = new FitContext(true);

            var circle = Strategies2D.BestPrimitive(Outline(Polygon(float2.zero, 1f, 48)), context, out var circleFill);
            var box = Strategies2D.BestPrimitive(Outline(Rectangle(float2.zero, new float2(2, 1), 0)), context, out var boxFill);
            var capsule = Strategies2D.BestPrimitive(Outline(Stadium(0.5f, 3f, true)), context, out _);

            Assert.That(circle.Kind, Is.EqualTo(ShapeKind2D.Circle));
            Assert.That(circleFill, Is.GreaterThan(0.95f));
            Assert.That(box.Kind, Is.EqualTo(ShapeKind2D.Box));
            Assert.That(boxFill, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(capsule.Kind, Is.EqualTo(ShapeKind2D.Capsule));
        }

        [Test]
        public void RotatedBox_OnlyWhenRotationAllowed()
        {
            var outline = Outline(Rectangle(float2.zero, new float2(3, 0.5f), 25f));

            var rotated = Strategies2D.Box(outline, new FitContext(true));
            var axisAligned = Strategies2D.Box(outline, new FitContext(false));

            Assert.That(rotated.IsRotated, Is.True);
            Assert.That(rotated.Area, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(axisAligned.IsRotated, Is.False);
            Assert.That(axisAligned.Area, Is.GreaterThan(rotated.Area));
        }
    }
}
