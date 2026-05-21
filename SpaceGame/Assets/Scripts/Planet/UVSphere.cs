using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteInEditMode]
public class UVSphere : MonoBehaviour
{
    [Range(4, 256)] public int latSegments = 64;
    [Range(4, 256)] public int lonSegments = 64;
    public float radius = 1f;

    void Awake() => Generate();

#if UNITY_EDITOR
    void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this) Generate(); };
#endif

    public void Generate()
    {
        int rings = latSegments + 1;   // rows of vertices (pole-to-pole)
        int cols  = lonSegments + 1;   // columns — extra column closes the seam with correct UVs

        Vector3[] vertices = new Vector3[rings * cols];
        Vector3[] normals  = new Vector3[rings * cols];
        Vector2[] uvs      = new Vector2[rings * cols];

        for (int lat = 0; lat < rings; lat++)
        {
            float phi = Mathf.PI * lat / latSegments;   // 0 (north) → PI (south)
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int lon = 0; lon < cols; lon++)
            {
                float theta = 2f * Mathf.PI * lon / lonSegments;  // 0 → 2PI

                float x =  sinPhi * Mathf.Cos(theta);
                float y =  cosPhi;
                float z =  sinPhi * Mathf.Sin(theta);

                int i = lat * cols + lon;
                vertices[i] = new Vector3(x, y, z) * radius;
                normals[i]  = new Vector3(x, y, z);
                uvs[i]      = new Vector2((float)lon / lonSegments, 1f - (float)lat / latSegments);
            }
        }

        // Build triangles — each quad becomes 2 triangles
        int[] tris = new int[latSegments * lonSegments * 6];
        int t = 0;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int a = lat * cols + lon;
                int b = a + cols;
                int c = a + 1;
                int d = b + 1;

                // Triangle 1 — CCW from outside = outward normal
                tris[t++] = a;
                tris[t++] = c;
                tris[t++] = b;

                // Triangle 2
                tris[t++] = c;
                tris[t++] = d;
                tris[t++] = b;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "UV Sphere";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = vertices;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
