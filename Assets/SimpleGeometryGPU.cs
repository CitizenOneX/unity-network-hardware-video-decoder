using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleGeometryGPU : MonoBehaviour
{
    public ComputeShader computeShader;
    public Shader shader;
    private Material material;

    private GraphicsBuffer vertexBuffer;

    // Start is called before the first frame update
    void Start()
    {
        // 3 float4s for now as 12 floats
        // Needs to be raw; using it as a Vertex target didn't seem to work
        // with the shader how I expected...
        vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 3, sizeof(float) * 4);
        vertexBuffer.SetData(new float[] { 1, 0, 0 , 1, 0, 0, 0, 1, 0, 1, 0, 1 });
        computeShader.SetBuffer(0, "vertices", vertexBuffer);

        computeShader.Dispatch(0, 3, 1, 1); // TODO: try 3,1,1 and dispatch compute kernel once for each vertex
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnRenderObject()
    {
        if (vertexBuffer == null)
            return;

        if (material == null)
        {
            material = new Material(shader);
            material.hideFlags = HideFlags.DontSave;
        }

        material.SetPass(0);
        material.SetMatrix("transform", transform.localToWorldMatrix);
        material.SetBuffer("vertices", vertexBuffer);

        Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
    }

    private void OnDestroy()
    {
        vertexBuffer?.Release();

        if (material != null)
        {
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }
    }
}
