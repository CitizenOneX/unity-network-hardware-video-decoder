Shader "Unlit/SimpleUnlitShader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // include debug symbols for RenderDoc
            #pragma enable_d3d11_debug_symbols

            #include "UnityCG.cginc"

            StructuredBuffer<float4> vertices;

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            float4x4 transform; //typically local to world matrix

            v2f vert (uint vid : SV_VertexID)
            {
                v2f o;

                //float3 vin = vertices[vid];
                // local to world transform
                //o.vertex = mul(transform, vin);
                //o.vertex = UnityObjectToClipPos(o.vertex);
                o.vertex = UnityObjectToClipPos(vertices[vid]);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(1,1,1,1);
            }
            ENDCG
        }
    }
}
