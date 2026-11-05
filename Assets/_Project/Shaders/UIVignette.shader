// 暗角（Vignette）：全屏 UI Image 用的 shader——中心透、四角渐暗。
// 用法：建材质 → 全屏 Image 挂材质（Raycast Target 关）。参数在材质上调。
Shader "SOS/UI_Vignette"
{
    Properties
    {
        _VignetteColor ("暗角颜色", Color) = (0, 0, 0, 1)
        _Intensity ("强度", Range(0, 1)) = 0.45
        _Radius ("半径（小于它完全透明）", Range(0.2, 1.5)) = 0.75
        _Softness ("柔和度（过渡带宽窄）", Range(0.01, 1)) = 0.45
        _CenterX ("中心偏移X", Range(-0.5, 0.5)) = 0
        _CenterY ("中心偏移Y", Range(-0.5, 0.5)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _VignetteColor;
            float _Intensity, _Radius, _Softness, _CenterX, _CenterY;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;   // Image 的 Color / CanvasGroup 的 Alpha 从这走
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 按屏幕比例修正后的"到中心距离"，屏幕左右边缘 ≈ 1，角落略大于 1
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = (i.uv - float2(0.5 + _CenterX, 0.5 + _CenterY)) * float2(aspect, 1.0);
                float dist = length(d) / (0.5 * aspect);

                // 半径内全透明，半径+柔和度外全暗，中间平滑过渡
                float vig = smoothstep(_Radius, _Radius + _Softness, dist);

                fixed4 col = _VignetteColor;
                col.a = col.a * vig * _Intensity * i.color.a;
                return col;
            }
            ENDCG
        }
    }
}
