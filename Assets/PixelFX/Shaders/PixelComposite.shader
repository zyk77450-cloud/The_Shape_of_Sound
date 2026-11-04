// 等价于 JS 版 composite(frame, imageData.data, palette) 的 GPU 实现：
// 颜色 × (环境光 + 光照层)，可选色阶量化模拟复古调色板。
// 用在 RawImage / 材质上：_MainTex = 颜色层贴图，_LightTex = 光照层贴图。
Shader "PixelFX/Composite"
{
    Properties
    {
        _MainTex ("Color", 2D) = "white" {}
        _LightTex ("Light", 2D) = "black" {}
        _Ambient ("Ambient", Range(0, 1)) = 0.4
        _LightIntensity ("Light Intensity", Range(0, 4)) = 1
        _ColorLevels ("Color Levels (0=off)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        ZTest Off
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;   // 颜色层
            sampler2D _LightTex;  // 光照层（r 通道 = 光强）
            float _Ambient;
            float _LightIntensity;
            float _ColorLevels;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float l = saturate(_Ambient + tex2D(_LightTex, i.uv).r * _LightIntensity);

                float3 rgb = c.rgb * l;

                // 色阶量化：模拟复古调色板（_ColorLevels < 2 时关闭）
                if (_ColorLevels >= 2.0)
                {
                    float n = _ColorLevels - 1.0;
                    rgb = floor(rgb * n + 0.5) / n;
                }

                return fixed4(rgb, c.a);
            }
            ENDCG
        }
    }
}
