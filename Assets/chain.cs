using UnityEngine;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class HoseSplineBend : MonoBehaviour
{
    public Transform[] controlPoints; // ordered base -> tip, placed along the hose's current curve

    const int SplineSamples = 64;

    Mesh mesh;
    Vector3[] restVertices;
    float[] vertexT;
    Vector3[] vertexLocalOffset;
    Vector3[] samplePositions = new Vector3[SplineSamples];
    Quaternion[] sampleRotations = new Quaternion[SplineSamples];
    Vector3[] workingVertices;
    Vector3[] controlLocalScratch;

    void Start()
    {
        mesh = GetComponent<MeshFilter>().mesh; // auto-instanced, won't touch the shared asset
        restVertices = mesh.vertices;

        var restControlLocal = new Vector3[controlPoints.Length];
        for (int i = 0; i < controlPoints.Length; i++)
            restControlLocal[i] = transform.InverseTransformPoint(controlPoints[i].position);

        SampleSpline(restControlLocal, samplePositions, sampleRotations);

        vertexT = new float[restVertices.Length];
        vertexLocalOffset = new Vector3[restVertices.Length];

        for (int v = 0; v < restVertices.Length; v++)
        {
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int s = 0; s < SplineSamples; s++)
            {
                float d = (restVertices[v] - samplePositions[s]).sqrMagnitude;
                if (d < bestDist) { bestDist = d; bestIdx = s; }
            }
            vertexT[v] = bestIdx / (float)(SplineSamples - 1);
            vertexLocalOffset[v] = Quaternion.Inverse(sampleRotations[bestIdx]) *
                                    (restVertices[v] - samplePositions[bestIdx]);
        }

        controlLocalScratch = new Vector3[controlPoints.Length];
        workingVertices = new Vector3[restVertices.Length];
    }

    void LateUpdate()
    {
        for (int i = 0; i < controlPoints.Length; i++)
            controlLocalScratch[i] = transform.InverseTransformPoint(controlPoints[i].position);

        SampleSpline(controlLocalScratch, samplePositions, sampleRotations);

        for (int v = 0; v < restVertices.Length; v++)
        {
            float sampleF = vertexT[v] * (SplineSamples - 1);
            int i0 = Mathf.FloorToInt(sampleF);
            int i1 = Mathf.Min(i0 + 1, SplineSamples - 1);
            float frac = sampleF - i0;

            Vector3 pos = Vector3.Lerp(samplePositions[i0], samplePositions[i1], frac);
            Quaternion rot = Quaternion.Slerp(sampleRotations[i0], sampleRotations[i1], frac);

            workingVertices[v] = pos + rot * vertexLocalOffset[v];
        }

        mesh.vertices = workingVertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void SampleSpline(Vector3[] controlLocal, Vector3[] outPositions, Quaternion[] outRotations)
    {
        for (int s = 0; s < SplineSamples; s++)
        {
            float t = s / (float)(SplineSamples - 1);
            outPositions[s] = CatmullRom(controlLocal, t);
            Vector3 tangent = (CatmullRom(controlLocal, Mathf.Min(t + 0.01f, 1f)) -
                                CatmullRom(controlLocal, Mathf.Max(t - 0.01f, 0f))).normalized;
            // flip reference "up" when tangent nearly matches it, to avoid the frame flipping/snapping
            Vector3 up = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            outRotations[s] = Quaternion.LookRotation(tangent, up);
        }
    }

    Vector3 CatmullRom(Vector3[] pts, float t)
    {
        int n = pts.Length;
        float scaledT = t * (n - 1);
        int i1 = Mathf.Clamp(Mathf.FloorToInt(scaledT), 0, n - 1);
        int i0 = Mathf.Clamp(i1 - 1, 0, n - 1);
        int i2 = Mathf.Clamp(i1 + 1, 0, n - 1);
        int i3 = Mathf.Clamp(i1 + 2, 0, n - 1);
        float localT = scaledT - i1;
        Vector3 p0 = pts[i0], p1 = pts[i1], p2 = pts[i2], p3 = pts[i3];

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * localT +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * localT * localT +
            (-p0 + 3f * p1 - 3f * p2 + p3) * localT * localT * localT
        );
    }
}
