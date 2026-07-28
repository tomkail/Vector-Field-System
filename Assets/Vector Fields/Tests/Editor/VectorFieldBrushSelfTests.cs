using NUnit.Framework;
using UnityEngine;

namespace VectorFields {
    // Regression checks for the brush ops and shape falloff — the pure, bug-prone parts of the painting system (an
    // op-level check like this would have caught the inverted Erase). Run from the Test Runner (EditMode,
    // VectorFields.Tests.Editor).
    // (The stroke geometry — coverage accumulation, frame-rate independence — needs an in-scene/play test; not covered here.)
    public class VectorFieldBrushSelfTests {
        const float Eps = 1e-3f;

        // Helper: brushForce/finalForce carry the weight as magnitude (ctx.Weight); strokeForce is the unit direction.
        static BrushApplyContext<Vector2> Ctx(Vector2 current, Vector2 dir, float weight, float pressure, Vector2Int gp, Vector2 center) {
            Vector2 u = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.zero;
            Vector2 f = u * weight;
            return new BrushApplyContext<Vector2>(current, f, f, u, pressure, gp, center, null);
        }

        static void AssertApprox(Vector2 expected, Vector2 actual, string message) =>
            Assert.LessOrEqual((actual - expected).magnitude, Eps, $"{message} (expected {expected}, got {actual})");

        static readonly Vector2Int Origin = new Vector2Int(0, 0);

        // --- Ops: construct a per-cell context and assert the op's core behaviour ------------------------------------

        [Test]
        public void Draw_OnEmptyCellAtFullWeight_IsStrokeDirectionTimesPressure() {
            AssertApprox(Vector2.right, VectorFieldBrushOpRegistry.Draw.Apply(
                Ctx(Vector2.zero, Vector2.right, 1f, 1f, Origin, Vector2.zero)),
                "Draw on an empty cell at weight 1 should be the stroke direction * pressure");
        }

        // Guards the inversion bug: full weight+pressure clears the cell; zero weight leaves it untouched.
        [Test]
        public void Erase_AtFullWeightAndPressure_ClearsTheCell() {
            AssertApprox(Vector2.zero, VectorFieldBrushOpRegistry.Erase.Apply(
                Ctx(new Vector2(2f, 0f), Vector2.right, 1f, 1f, Origin, Vector2.zero)),
                "Erase at weight 1, pressure 1 should clear the cell (strongest at the centre)");
        }

        [Test]
        public void Erase_AtZeroWeight_LeavesTheCellUnchanged() {
            AssertApprox(new Vector2(2f, 0f), VectorFieldBrushOpRegistry.Erase.Apply(
                Ctx(new Vector2(2f, 0f), Vector2.right, 0f, 1f, Origin, Vector2.zero)),
                "Erase at weight 0 should leave the cell unchanged");
        }

        [Test]
        public void Additive_AddsTheBrushVectorToTheCurrentValue() {
            AssertApprox(new Vector2(1f, 1f), VectorFieldBrushOpRegistry.Additive.Apply(
                Ctx(new Vector2(1f, 0f), Vector2.up, 1f, 1f, Origin, Vector2.zero)),
                "Additive should add the brush vector to the current value");
        }

        [Test]
        public void Burn_IncreasesMagnitude() {
            Assert.Greater(VectorFieldBrushOpRegistry.Burn.Apply(
                Ctx(new Vector2(1f, 0f), Vector2.right, 1f, 1f, Origin, Vector2.zero)).magnitude, 1f + Eps);
        }

        [Test]
        public void Dodge_DecreasesMagnitude() {
            Assert.Less(VectorFieldBrushOpRegistry.Dodge.Apply(
                Ctx(new Vector2(2f, 0f), Vector2.right, 1f, 1f, Origin, Vector2.zero)).magnitude, 2f - Eps);
        }

        [Test]
        public void Clamp_CapsMagnitudeAtThePressureValue() {
            Assert.LessOrEqual(VectorFieldBrushOpRegistry.Clamp.Apply(
                Ctx(new Vector2(3f, 0f), Vector2.right, 1f, 1f, Origin, Vector2.zero)).magnitude, 1f + Eps);
        }

        [Test]
        public void Normalize_DrivesMagnitudeToThePressureValue() {
            Assert.LessOrEqual(Mathf.Abs(VectorFieldBrushOpRegistry.Normalize.Apply(
                Ctx(new Vector2(0.5f, 0f), Vector2.right, 1f, 1f, Origin, Vector2.zero)).magnitude - 1f), Eps);
        }

        // Radial ops derive direction from the offset to the brush centre (they ignore the stroke direction), so `dir`
        // below just carries weight = 1. Cell at (2,0), centre at origin:

        [Test]
        public void Repel_PointsOutwardFromTheCentre() {
            AssertApprox(Vector2.right, VectorFieldBrushOpRegistry.Repel.Apply(
                Ctx(Vector2.zero, Vector2.right, 1f, 1f, new Vector2Int(2, 0), Vector2.zero)),
                "Repel should point outward (away from the centre)");
        }

        [Test]
        public void Attract_PointsInwardTowardTheCentre() {
            AssertApprox(Vector2.left, VectorFieldBrushOpRegistry.Attract.Apply(
                Ctx(Vector2.zero, Vector2.right, 1f, 1f, new Vector2Int(2, 0), Vector2.zero)),
                "Attract should point inward (toward the centre)");
        }

        [Test]
        public void Swirl_PointsTangentAroundTheCentre() {
            AssertApprox(Vector2.up, VectorFieldBrushOpRegistry.Swirl.Apply(
                Ctx(Vector2.zero, Vector2.right, 1f, 1f, new Vector2Int(2, 0), Vector2.zero)),
                "Swirl should point tangent (90 deg CCW) around the centre");
        }

        // --- Shape falloff -------------------------------------------------------------------------------------------

        [Test]
        public void RadialShape_FalloffProfile() {
            var soft = BrushShape.Radial(0.5f);
            Assert.LessOrEqual(Mathf.Abs(soft.Weight(0f) - 1f), Eps, "Radial weight at the centre should be 1");
            Assert.LessOrEqual(Mathf.Abs(soft.Weight(1f)), Eps, "Radial weight at the edge should be 0");
            Assert.That(soft.Weight(0.75f), Is.GreaterThan(Eps).And.LessThan(1f - Eps),
                "Radial weight inside the falloff band should be between 0 and 1");
            Assert.GreaterOrEqual(soft.Weight(0.2f), soft.Weight(0.8f),
                "Radial weight should be monotonically non-increasing outward");
        }

        // --- Registry ------------------------------------------------------------------------------------------------

        [Test]
        public void Registry_NamedAccessorsMatchTheirIdsAndInstances() {
            Assert.AreEqual("draw", VectorFieldBrushOpRegistry.Draw.Id);
            Assert.AreEqual("erase", VectorFieldBrushOpRegistry.Erase.Id);
            Assert.IsTrue(ReferenceEquals(VectorFieldBrushOpRegistry.ById("repel"), VectorFieldBrushOpRegistry.Repel),
                "ById should return the same instance as the named accessor");
        }
    }
}
