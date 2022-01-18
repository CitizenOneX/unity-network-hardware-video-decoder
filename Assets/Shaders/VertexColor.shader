// Original code source:
// http://www.kamend.com/2014/05/rendering-a-point-cloud-inside-unity/
// Modifications:
// - point size varying with distance

Shader "Custom/VertexColor" {
    SubShader {
    Pass {
        LOD 200
         
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.5

        #include "UnityCG.cginc"
 
        struct VertexInput {
            float4 pos : POSITION;
            float4 col: COLOR;
        };
         
        struct VertexOutput {
            float4 pos : SV_POSITION;
            float4 col : COLOR;
            float size : PSIZE;
        };
         
        VertexOutput vert(VertexInput vin) {
         
            VertexOutput o;

            o.pos = UnityObjectToClipPos(vin.pos);
            //o.col = float4(vin.col.r, vin.col.g, v.col.b, 1.0f);
            o.col = vin.col;
            // temp override 
            //o.col = float4(1.0f, 0f, 0.5f, 1.0f);
            o.size = 4.0; //disable size computation for now
            return o;
        }
         
        float4 frag(VertexOutput o) : COLOR {
            return o.col;
        }
 
        ENDCG
        } 
    }
}

