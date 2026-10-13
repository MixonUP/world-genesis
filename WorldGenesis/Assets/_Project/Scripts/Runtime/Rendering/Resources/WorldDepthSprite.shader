Shader "World Genesis/Depth Sprite"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _WriteDepth ("Write depth", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        Lighting Off
        ZWrite [_WriteDepth]
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * input.color;
                // Transparent texels never hide a person through an invisible sprite rectangle.
                clip(color.a - .0039);
                return color;
            }
            ENDCG
        }
    }
}
