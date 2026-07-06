using UnityEngine;

// A surface the generic painting core (PaintStroke<T>, the brush kernel) can paint into, independent of what kind of
// value the field holds. DrawableVectorFieldComponent implements IPaintTarget<Vector2>; a smoke/colour field
// implements IPaintTarget<Color>. The stroke talks to this a few times per span (never per cell), so the interface
// dispatch here is off the hot path — the per-cell Get/Set happens directly on the concrete FieldMap<T> it hands back.
public interface IPaintTarget<T> {
	// Grid<->world mapping used to turn stroke world positions into grid positions.
	GridTransform grid { get; }

	// The field being painted. A FieldMap<T> subclass (VectorFieldMap / ColorFieldMap) so per-cell Get/Set are direct calls
	// and bilinear GetValueAtGridPosition uses the subclass's Lerp.
	FieldMap<T> PaintField { get; }

	// Create a fresh, correctly-typed empty map of the given size (VectorFieldMap / ColorFieldMap). Used for the stroke's
	// pooled pre-stroke snapshot (neighbour-reading ops) so it's the right subtype for bilinear sampling.
	FieldMap<T> CreateMap(Vector2Int size);

	// Report the grid rect touched by a paint step so the target can upload just that region.
	void MarkRegionDirty(RectInt region);
}
