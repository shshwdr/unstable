Shader "Unstable/SpriteSolidFill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FillAmount ("Fill Amount", Range(0,1)) = 1
        _MinY ("Min Y", Float) = -0.5
        _MaxY ("Max Y", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _FillAmount;
            float _MinY;
            float _MaxY;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float localY : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                o.localY = v.vertex.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float range = max(0.0001, _MaxY - _MinY);
                float t = (i.localY - _MinY) / range;
                clip(_FillAmount - t);

                fixed a = tex2D(_MainTex, i.texcoord).a * i.color.a;
                return fixed4(i.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
