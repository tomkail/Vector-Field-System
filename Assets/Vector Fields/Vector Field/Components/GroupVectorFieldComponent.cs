using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GroupVectorFieldComponent : VectorFieldComponent {
    [System.Serializable]
    public class VectorFieldLayer {
        public VectorFieldComponent component;
        
        [Range(0,1)]
        public float strength = 1;
    
        public BlendMode blendMode = BlendMode.Add;
        public enum BlendMode {
            // Add to current value
            Add,
            // Lerp between current and new value based on brush alpha
            Blend
        }
	
        public Component components = Component.All;
        [Flags]
        public enum Component {
            None = 0,
            All = ~0,
            // Add to current value
            Magnitude = 1 << 0,
            // Lerp between current and new value based on brush alpha
            Direction = 1 << 1,
        }
        
    }

    public List<VectorFieldLayer> layers = new List<VectorFieldLayer>();
    IEnumerable<VectorFieldComponent> childComponents => this.GetComponentsX(ComponentX.ComponentSearchParams<VectorFieldComponent>.AllDescendentsExcludingSelf(true));

    void RefreshLayers() {
        layers.RemoveAll(x => x.component == null);
        List<VectorFieldComponent> added = new List<VectorFieldComponent>();
        List<VectorFieldComponent> removed = new List<VectorFieldComponent>();
        IEnumerableX.GetChanges(childComponents, layers.Select(x => x.component), ref added, ref removed);
        foreach (var component in added) {
            layers.Add(new VectorFieldLayer() {
                component = component
            });
        }
        foreach (var component in removed) {
            layers.RemoveAll(x => x.component == component);
        }
        layers = layers.OrderBy(x => x.component.transform.GetHeirarchyIndex()).ToList();
    }

    protected override void RenderInternal() {
        RefreshLayers();
        
        // For performance we should iterate layers first, then iterate points.
        // For each layer we should first determine the points on both canvases that are in the overlap.
        // var points = gridRenderer.GetPointsInWorldBounds(child.transform.GetBounds());
        
        vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
        // var points = vectorField.Points();

        foreach (var child in layers) {
            if(!child.component.isActiveAndEnabled) continue;
            if(child.strength <= 0) continue;
            var points = gridRenderer.GetPointsInWorldBounds(child.component.GetBounds());
            foreach (var point in points) {
                Vector2 vectorFieldForce = vectorField.GetValueAtGridPoint(point);
                
                var pointWorldPosition = gridRenderer.cellCenter.GridToWorldPoint(point);
                Vector2 affectorForce = transform.InverseTransformDirection(child.component.EvaluateWorldVector(pointWorldPosition));
                Vector2 finalForce = Vector2.zero;
                
                if (child.blendMode == VectorFieldLayer.BlendMode.Add) {
                    if (child.components.HasFlag(VectorFieldLayer.Component.All)) finalForce = vectorFieldForce + affectorForce * child.strength;
                    else if (child.components.HasFlag(VectorFieldLayer.Component.Direction)) finalForce = affectorForce + vectorFieldForce.magnitude * affectorForce.normalized * child.strength;
                    else if (child.components.HasFlag(VectorFieldLayer.Component.Magnitude)) finalForce = vectorFieldForce + vectorFieldForce.normalized * affectorForce.magnitude * child.strength;
                }

                if (child.blendMode == VectorFieldLayer.BlendMode.Blend) {
                    if (child.components.HasFlag(VectorFieldLayer.Component.All)) finalForce = Vector2.Lerp(vectorFieldForce, affectorForce, child.strength);
                    else if (child.components.HasFlag(VectorFieldLayer.Component.Direction)) finalForce = affectorForce.normalized * child.strength;
                    else if (child.components.HasFlag(VectorFieldLayer.Component.Magnitude)) finalForce = vectorFieldForce.normalized * child.strength;
                }

                vectorFieldForce = finalForce;
                vectorField.SetValueAtGridPoint(point, vectorFieldForce);
            }

        }
    }
}