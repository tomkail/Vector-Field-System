using UnityEngine;
using System.Collections;

[RequireComponent(typeof(ParticleSystem))]
public class LocalisedGridParticles : MonoBehaviour {


	void Awake() {
		particles = GetComponent<ParticleSystem>();
	}

	void Start () {
		particles.Clear();
		emitters = new Emitter[maximumEmitters];
		numberOfEmitters = 0;
	}

	public float fieldVelocityMultipler = 4;
	[MinMaxAttribute(0, 2)]
	public Vector2 randomFieldVelocityMultiplier = Vector2.one;


	public float randomHeightMagnitude = 1;

	public int resolutionX = 40;
	public int resolutionZ = 40;

	public float thresholdValue = 20.0f;

	[DisableAttribute]
	public int numberOfParticles;

	public int maximumEmitters = 16000;
	public float emissionRate = 0.01f;
	public float emitterProbability = 1.0f;

	[DisableAttribute]
	public int numberOfEmitters;
	Emitter[] emitters;

	struct Emitter {
		public Vector3 position;
		public Vector3 velocity;

		public Emitter(Vector3 position, Vector3 velocity) {
			this.position = position;
			this.velocity = velocity;
		}
	}
		
	void Update () {

		FillNewRegionsWithEmitters ();

		UpdateEmitters ();

		// For viewing in inspector
		numberOfParticles = particles.particleCount;
	}

	// Delete emitters that are no longer within the current rect, and emit particles
	void UpdateEmitters ()
	{
		var emitParams = new ParticleSystem.EmitParams();

		float emitterInterval = this.transform.localScale.x / resolutionX;
		Rect currentEmissionRect = currentRect;
		for (int i = 0; i < numberOfEmitters;) {
			var emitterPos = emitters [i].position;

			// Emitter gone out of bounds?
			if (   emitterPos.x < currentEmissionRect.xMin || emitterPos.x > currentEmissionRect.xMax 
				|| emitterPos.z < currentEmissionRect.yMin || emitterPos.z > currentEmissionRect.yMax) {
				DeleteEmitter (i);
				continue;
			}

			// Emit a particle from this emitter?
			else {
				if (Random.value < emissionRate) {
					Vector2 randomOffset2d = 0.5f * emitterInterval * Random.insideUnitCircle;
					Vector2 gridPosition = VectorFieldManager.Instance.gridRenderer.cellCenter.WorldToGridPosition(emitterPos + new Vector3 (randomOffset2d.x, 0.0f, randomOffset2d.y));
					Vector3 gridNormal = Vector3.up;
					float randomHeight = Random.Range(-emitters [i].velocity.magnitude, emitters [i].velocity.magnitude) * randomHeightMagnitude;
					emitParams.position = VectorFieldManager.Instance.gridRenderer.cellCenter.GridToWorldPoint(gridPosition) + gridNormal * randomHeight;
					emitParams.velocity = Vector3.ProjectOnPlane(emitters [i].velocity, gridNormal) * fieldVelocityMultipler * Random.Range(randomFieldVelocityMultiplier.x, randomFieldVelocityMultiplier.y);

//					emitParams.startSize = baseSize + (Random.value - 0.5f) * baseSize;
//					emitParams.startColor = new Color (1.0f, 1.0f, 1.0f, Random.value * 0.2f + 0.05f);

					emitParams.rotation3D = Quaternion.LookRotation(gridNormal).eulerAngles;
					particles.Emit(emitParams, 1);
				}
			}
			// Only increment if we didn't just delete an emitter
			i++;
		}
	}

	void FillNewRegionsWithEmitters ()
	{
		Rect newRect = currentRect;

		// Initial setup
		if (previousRect.Equals (new Rect ())) {
			FillRectWithEmitters (newRect);
		}

		// New horizontal/vertical rects on each side of old rect (e.g. L piece that remains when subtracting old rect from new)
		else if (!newRect.Equals (previousRect)) {
			// Calculate new rect regions
			Rect newHorizontalSlice = new Rect ();
			// New slice on left
			if (newRect.xMin < previousRect.xMin) {
				newHorizontalSlice.xMin = newRect.xMin;
				newHorizontalSlice.xMax = previousRect.xMin;
			}
			// New slice on right
			else if (newRect.xMax > previousRect.xMax) {
				newHorizontalSlice.xMax = newRect.xMax;
				newHorizontalSlice.xMin = previousRect.xMax;
			}

			// Allow vertical slice to take corner, not the horizontal slice
			newHorizontalSlice.yMin = Mathf.Max (previousRect.yMin, newRect.yMin);
			newHorizontalSlice.yMax = Mathf.Min (previousRect.yMax, newRect.yMax);

			Rect newVerticalSlice = new Rect ();
			// New slice above
			if (newRect.yMin < previousRect.yMin) {
				newVerticalSlice.yMin = newRect.yMin;
				newVerticalSlice.yMax = previousRect.yMin;
			}
			// New slice below
			else if (newRect.yMax > previousRect.yMax) {
				newVerticalSlice.yMax = newRect.yMax;
				newVerticalSlice.yMin = previousRect.yMax;
			}
			newVerticalSlice.xMin = newRect.xMin;
			newVerticalSlice.xMax = newRect.xMax;
			FillRectWithEmitters (newHorizontalSlice);
			FillRectWithEmitters (newVerticalSlice);
		}
		previousRect = newRect;
	}

	void FillRectWithEmitters(Rect rect) {

		if( rect.width == 0.0f || rect.height == 0.0f)
			return;
		
		float emitterPerWorldUnit = resolutionX / this.transform.localScale.x;

		int xStart = Mathf.CeilToInt(rect.xMin * emitterPerWorldUnit);
		int xEnd = Mathf.FloorToInt(rect.xMax * emitterPerWorldUnit);

		int zStart = Mathf.CeilToInt(rect.yMin * emitterPerWorldUnit);
		int zEnd = Mathf.FloorToInt(rect.yMax * emitterPerWorldUnit);

		// Create the particles in the locations specified by the dithering algorithm
		for(int x=xStart; x<=xEnd; x++) {
			for(int y=zStart; y<=zEnd; y++) {

				float posX = x / emitterPerWorldUnit;
				float posZ = y / emitterPerWorldUnit;

				Vector3 samplePosition = new Vector3(posX, 0.0f, posZ);

				Vector2 vectorFieldValue = VectorFieldManager.Instance.EvaluateVector(samplePosition);

				float vectorMagnitude = vectorFieldValue.magnitude;
			
				if( vectorMagnitude > thresholdValue && Random.value < emitterProbability ) {

					var position = new Vector3(posX, posZ, 0);
					var emitterVelocity =  new Vector3(0.1f * vectorFieldValue.x, 0, 0.1f * vectorFieldValue.y);
					CreateEmitter(position, emitterVelocity);

//					CreateEmitter(SpaceGameWorld.Instance.NormalizedVectorToWorldVector(gridPosNorm), new Vector3(vectorFieldValue.x, 0, vectorFieldValue.y));
				}
			}
		}
			
	}

	void CreateEmitter(Vector3 position, Vector3 velocity) {
//		Debug.Assert(numberOfEmitters < maximumEmitters, "Trying to create an emitter but exceeded maximum "+transform.HierarchyPath());
		if( numberOfEmitters < maximumEmitters ) {
			emitters[numberOfEmitters] = new Emitter(position, velocity);
			numberOfEmitters++;
		}
	}

	void DeleteEmitter(int index) {
		Debug.Assert(index < numberOfEmitters, "Trying to delete an emitter out of range");
		if( index < numberOfEmitters - 1 ) {
			emitters[index] = emitters[numberOfEmitters-1];
		}
		numberOfEmitters--;
	}
		

	Rect currentRect {
		get {
			return new Rect(
				this.transform.position.x - 0.5f*this.transform.localScale.x,
				this.transform.position.z - 0.5f*this.transform.localScale.z,
				this.transform.localScale.x,
				this.transform.localScale.z
			);
		}
	}

	Rect previousRect;
	ParticleSystem particles;
}
