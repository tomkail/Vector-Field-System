using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// A crowd destination ("attraction") — a ride, stall or gate agents walk toward. Its world position is the goal
    /// for one <see cref="FlowFieldSolver"/>; <see cref="CrowdFlowManager"/> owns the solving and the field. This is
    /// just the authored marker: where it is, how it reads (colour), and how long visitors linger before moving on.
    /// </summary>
    public class Attraction : MonoBehaviour {
        [Tooltip("Display/tint colour for this destination's crowd stream and its flow visualisation.")]
        public Color color = Color.cyan;
        [Tooltip("How long an agent dwells here on arrival before choosing a new attraction.")]
        public Vector2 dwellTimeRange = new Vector2(2f, 5f);
        [Tooltip("Arrival radius (world units): an agent this close counts as arrived.")]
        public float arriveRadius = 2f;

        /// <summary>Assigned by the manager: which flow field (destination index) this attraction owns.</summary>
        [System.NonSerialized] public int fieldIndex = -1;

        public float RandomDwell() => Random.Range(dwellTimeRange.x, dwellTimeRange.y);

        void OnDrawGizmos() {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, arriveRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3f);
        }
    }
}
