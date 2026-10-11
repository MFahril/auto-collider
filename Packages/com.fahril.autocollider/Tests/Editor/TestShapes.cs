using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    /// <summary>Point clouds with known answers, plus containment assertions.</summary>
    internal static class TestShapes
    {
        public static float3[] BoxCorners(float3 size, quaternion rotation = default, float3 center = default)
        {
            if (rotation.value.Equals(float4.zero)) rotation = quaternion.identity;
            var half = size * 0.5f;
            var points = new List<float3>();
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
                points.Add(center + math.mul(rotation, half * new float3(x, y, z)));
            return points.ToArray();
        }

        public static float3[] UvSphere(float radius, int latitude = 24, int longitude = 48, float3 center = default)
        {
            var points = new List<float3> { center + new float3(0, radius, 0), center - new float3(0, radius, 0) };
            for (var i = 1; i < latitude; i++)
            {
                var theta = math.PI * i / latitude;
                for (var j = 0; j < longitude; j++)
                {
                    var phi = 2 * math.PI * j / longitude;
                    points.Add(center + radius * new float3(math.sin(theta) * math.cos(phi), math.cos(theta), math.sin(theta) * math.sin(phi)));
                }
            }
            return points.ToArray();
        }

        /// <summary>Two rings of a cylinder along <paramref name="axis"/>.</summary>
        public static float3[] Cylinder(float radius, float height, float3 axis, int segments = 32)
        {
            axis = math.normalize(axis);
            GeometryUtils.Basis(axis, out var u, out var v);
            var points = new List<float3>();
            for (var ring = -1; ring <= 1; ring += 2)
            for (var s = 0; s < segments; s++)
            {
                var a = 2 * math.PI * s / segments;
                points.Add(axis * (ring * height * 0.5f) + radius * (u * math.cos(a) + v * math.sin(a)));
            }
            return points.ToArray();
        }

        public static float3[] RandomInBox(uint seed, int count, float3 extents)
        {
            var random = new Random(seed);
            var points = new float3[count];
            for (var i = 0; i < count; i++) points[i] = random.NextFloat3(-extents, extents);
            return points;
        }

        public static void AssertBoxContains(BoxFit box, IEnumerable<float3> points, float tolerance = 1e-4f)
        {
            var inverse = math.inverse(box.Rotation);
            foreach (var p in points)
            {
                var local = math.abs(math.mul(inverse, p - box.Center));
                Assert.That(math.all(local <= box.Size * 0.5f + tolerance), $"Point {p} outside box (local {local}, half {box.Size * 0.5f})");
            }
        }

        public static void AssertSphereContains(SphereFit sphere, IEnumerable<float3> points, float tolerance = 1e-4f)
        {
            foreach (var p in points)
                Assert.That(math.distance(p, sphere.Center), Is.LessThanOrEqualTo(sphere.Radius + tolerance), $"Point {p} outside sphere");
        }

        public static void AssertCapsuleContains(CapsuleFit capsule, IEnumerable<float3> points, float tolerance = 1e-4f)
        {
            var axisLocal = float3.zero;
            axisLocal[capsule.Direction] = 1;
            var axis = math.mul(capsule.Rotation, axisLocal);
            var half = math.max(0f, capsule.Height * 0.5f - capsule.Radius);
            var a = capsule.Center - axis * half;
            var b = capsule.Center + axis * half;
            foreach (var p in points)
            {
                var t = math.saturate(math.dot(p - a, b - a) / math.max(1e-12f, math.lengthsq(b - a)));
                var closest = a + (b - a) * t;
                Assert.That(math.distance(p, closest), Is.LessThanOrEqualTo(capsule.Radius + tolerance), $"Point {p} outside capsule");
            }
        }
    }
}
