using UnityEditor;
using UnityEngine;

// Runnable regression checks for the brush ops and shape falloff — the pure, bug-prone parts of the painting system
// (an op-level check like this would have caught the inverted Erase). Run via Tools > Vector Field > Run Brush
// Self-Tests; results are logged, failures as errors.
//
// Why a menu item and not a proper NUnit test: the project has no assembly definitions, so the code lives in the
// predefined Assembly-CSharp, which Unity's Test Runner assemblies can't reference. This Editor-folder class CAN
// reference it, so it's the pragmatic way to get runnable checks without migrating the whole project to asmdefs.
// (The stroke geometry — coverage accumulation, frame-rate independence — needs an in-scene/play test; not covered here.)
public static class VectorFieldBrushSelfTests {
    const float Eps = 1e-3f;

    [MenuItem("Tools/Vector Field/Run Brush Self-Tests")]
    public static void Run() {
        int pass = 0, fail = 0;
        void Check(bool cond, string msg) {
            if (cond) pass++;
            else { fail++; Debug.LogError($"[Vector Field self-test] FAIL: {msg}"); }
        }
        bool Approx(Vector2 a, Vector2 b) => (a - b).magnitude <= Eps;

        // --- Ops: construct a per-cell context and assert the op's core behaviour ------------------------------------
        // Helper: brushForce/finalForce carry the weight as magnitude (ctx.Weight); strokeForce is the unit direction.
        BrushApplyContext Ctx(Vector2 current, Vector2 dir, float weight, float pressure, Point gp, Vector2 center) {
            Vector2 u = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.zero;
            Vector2 f = u * weight;
            return new BrushApplyContext(current, f, f, u, pressure, gp, center, null);
        }
        var origin = new Point(0, 0);

        // Draw: sets an empty cell toward the stroke direction at full pressure.
        Check(Approx(VectorFieldBrushOpRegistry.Draw.Apply(
            Ctx(Vector2.zero, Vector2.right, 1f, 1f, origin, Vector2.zero)), Vector2.right),
            "Draw on an empty cell at weight 1 should be the stroke direction * pressure");

        // Erase: full weight+pressure clears the cell; zero weight leaves it untouched. (Guards the inversion bug.)
        Check(Approx(VectorFieldBrushOpRegistry.Erase.Apply(
            Ctx(new Vector2(2f, 0f), Vector2.right, 1f, 1f, origin, Vector2.zero)), Vector2.zero),
            "Erase at weight 1, pressure 1 should clear the cell (strongest at the centre)");
        Check(Approx(VectorFieldBrushOpRegistry.Erase.Apply(
            Ctx(new Vector2(2f, 0f), Vector2.right, 0f, 1f, origin, Vector2.zero)), new Vector2(2f, 0f)),
            "Erase at weight 0 should leave the cell unchanged");

        // Additive: adds the brush vector.
        Check(Approx(VectorFieldBrushOpRegistry.Additive.Apply(
            Ctx(new Vector2(1f, 0f), Vector2.up, 1f, 1f, origin, Vector2.zero)), new Vector2(1f, 1f)),
            "Additive should add the brush vector to the current value");

        // Burn grows magnitude along the current direction; Dodge shrinks it.
        Check(VectorFieldBrushOpRegistry.Burn.Apply(
            Ctx(new Vector2(1f, 0f), Vector2.right, 1f, 1f, origin, Vector2.zero)).magnitude > 1f + Eps,
            "Burn should increase magnitude");
        Check(VectorFieldBrushOpRegistry.Dodge.Apply(
            Ctx(new Vector2(2f, 0f), Vector2.right, 1f, 1f, origin, Vector2.zero)).magnitude < 2f - Eps,
            "Dodge should decrease magnitude");

        // Clamp caps magnitude at pressure; Normalize drives it to pressure.
        Check(VectorFieldBrushOpRegistry.Clamp.Apply(
            Ctx(new Vector2(3f, 0f), Vector2.right, 1f, 1f, origin, Vector2.zero)).magnitude <= 1f + Eps,
            "Clamp should cap magnitude at the pressure value");
        Check(Mathf.Abs(VectorFieldBrushOpRegistry.Normalize.Apply(
            Ctx(new Vector2(0.5f, 0f), Vector2.right, 1f, 1f, origin, Vector2.zero)).magnitude - 1f) <= Eps,
            "Normalize should drive magnitude to the pressure value");

        // Radial ops derive direction from the offset to the brush centre (they ignore the stroke direction), so `dir`
        // here just carries weight = 1. Cell at (2,0), centre at origin:
        var atRight = new Point(2, 0);
        Check(Approx(VectorFieldBrushOpRegistry.Repel.Apply(
            Ctx(Vector2.zero, Vector2.right, 1f, 1f, atRight, Vector2.zero)), Vector2.right),
            "Repel should point outward (away from the centre)");
        Check(Approx(VectorFieldBrushOpRegistry.Attract.Apply(
            Ctx(Vector2.zero, Vector2.right, 1f, 1f, atRight, Vector2.zero)), Vector2.left),
            "Attract should point inward (toward the centre)");
        Check(Approx(VectorFieldBrushOpRegistry.Swirl.Apply(
            Ctx(Vector2.zero, Vector2.right, 1f, 1f, atRight, Vector2.zero)), Vector2.up),
            "Swirl should point tangent (90 deg CCW) around the centre");

        // --- Shape falloff -------------------------------------------------------------------------------------------
        var soft = VectorFieldBrushShape.Radial(0.5f);
        Check(Mathf.Abs(soft.Weight(0f) - 1f) <= Eps, "Radial weight at the centre should be 1");
        Check(Mathf.Abs(soft.Weight(1f)) <= Eps, "Radial weight at the edge should be 0");
        Check(soft.Weight(0.75f) > Eps && soft.Weight(0.75f) < 1f - Eps,
            "Radial weight inside the falloff band should be between 0 and 1");
        Check(soft.Weight(0.2f) >= soft.Weight(0.8f), "Radial weight should be monotonically non-increasing outward");

        // --- Registry ------------------------------------------------------------------------------------------------
        Check(VectorFieldBrushOpRegistry.Draw.Id == "draw" && VectorFieldBrushOpRegistry.Erase.Id == "erase",
            "Named accessors should match their ids");
        Check(ReferenceEquals(VectorFieldBrushOpRegistry.ById("repel"), VectorFieldBrushOpRegistry.Repel),
            "ById should return the same instance as the named accessor");

        Debug.Log($"[Vector Field self-tests] {pass} passed, {fail} failed.");
    }
}
