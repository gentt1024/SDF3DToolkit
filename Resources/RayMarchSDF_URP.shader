Shader "Custom/RayMarchSDF_URP"
{
    Properties
    {
        _MainTex ("Texture", 3D) = "white" {}
        _VoxelSize ("Voxel Size", Float) = 0.003
        _Dimensions ("Dimensions", Vector) = (1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _ZeroFace ("Zero Face", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTestMode", Float) = 4
        
        _DebugX ("Debug X", Range(0, 1)) = 1
        _DebugY ("Debug Y", Range(0, 1)) = 1
        _DebugZ ("Debug Z", Range(0, 1)) = 1
        _DebugMaxDist ("Debug Max Dist", Float) = 10
        _DebugColor1 ("Debug Color 1", Color) = (1, 0, 0, 1)
        _DebugColor2 ("Debug Color 2", Color) = (0, 1, 0, 1)
        [Toggle] _DebugIsAuto ("Debug Is Auto", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }
        LOD 100

        ZWrite On
        ZTest [_ZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Front
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define ITERATIONS 100

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 local : TEXCOORD2;
            };

            Texture3D _MainTex;
            SamplerState sampler_MainTex;
            float3 _Dimensions;
            float _VoxelSize;
            float4 _Color;
            float _ZeroFace;

            float _DebugX;
            float _DebugY;
            float _DebugZ;
            float _DebugMaxDist;
            float4 _DebugColor1;
            float4 _DebugColor2;
            float _DebugIsAuto;

            bool intersect(float3 ro, float3 rd, float3 box_min, float3 box_max, out float t0, out float t1)
            {
                float3 invR = 1.0 / rd;
                float3 tbot = invR * (box_min - ro); // lower bound of intersection
                float3 ttop = invR * (box_max - ro); // upper bound of intersection
                float3 tmin = min(ttop, tbot);
                float3 tmax = max(ttop, tbot);

                float2 t = max(tmin.xx, tmin.yz);
                t0 = max(t.x, t.y);
                t = min(tmax.xx, tmax.yz);
                t1 = min(t.x, t.y);

                return t0 <= t1;
            }

            float sdf(float3 p)
            {
                static float3 object_scale = 1 / (_Dimensions * _VoxelSize);
                float3 p_US = p * object_scale;
                return _MainTex.Sample(sampler_MainTex, p_US).r;
            }
            
            float3 calculateNormal(float3 p)
            {
                static float2 eps = float2(_VoxelSize * 1.1, 0);
                float3 n;
                n.x = sdf(p + eps.xyy) - sdf(p - eps.xyy);
                n.y = sdf(p + eps.yxy) - sdf(p - eps.yxy);
                n.z = sdf(p + eps.yyx) - sdf(p - eps.yyx);
                return normalize(n);
            }

            float4 shade(float3 normal_WS)
            {
                // 获取场景光照
                Light mainLight = GetMainLight();
                float3 lightDir_WS = mainLight.direction;
                float3 lightColor = mainLight.color;
                float3 ambientColor = float3(0.1, 0.1, 0.1);
                float3 diffuseColor = lightColor * max(0, dot(normal_WS, lightDir_WS));
                float3 color = ambientColor + diffuseColor;
                return float4(color, 1);
            }

            struct MarchResult
            {
                float3 normal;
                float dist;
            };

            // ray marching in local space
            // ro: ray origin in local space
            // rd: ray direction in local space
            // max_dist: max distance to march in local space
            // return:
            //  color: color of the surface
            //  dist: distance to the surface in local space
            MarchResult rayMarch(in float3 ro, in float3 rd, float max_dist)
            {
                MarchResult result;
                result.normal = float4(0, 0, 0, 0);
                result.dist = -1;
                float t = 0.0;

                [loop] for (int i = 0; i < ITERATIONS; i++)
                {
                    float3 p = ro + t * rd;
                    float d = sdf(p);
                    if (d < _VoxelSize + _ZeroFace)
                    {
                        float3 normal = calculateNormal(p);
                        result.normal = normal;
                        result.dist = t;
                        return result;
                    }
                    t += d * 0.5f; // the factor here to avoid we go too far
                    
                    if (t > max_dist)
                    {
                        break;
                    }
                }
                
                return result;
            }

            struct FragOutput
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 vertex = v.vertex;
                vertex.xyz *= _Dimensions * _VoxelSize;
                o.vertex = TransformObjectToHClip(vertex);
                o.uv = v.uv;
                o.world = TransformObjectToWorld(vertex).xyz;
                o.local = vertex.xyz;
                return o;
            }

            FragOutput frag(Varyings i)
            {
                FragOutput o;
                
                static float3 ro_WS = GetCameraPositionWS();
                static float3 objectScale = _Dimensions * _VoxelSize;
                
                static float3 ro_LS = mul(unity_WorldToObject, float4(ro_WS, 1)).xyz;

                float3 rd_WS = normalize(i.world - ro_WS);
                float3 rd_LS = normalize(mul(unity_WorldToObject, float4(rd_WS, 0)).xyz);

                float tnear_LS;
                float tfar_LS;
                intersect(ro_LS, rd_LS, 0, objectScale, tnear_LS, tfar_LS);
                tnear_LS = max(tnear_LS, 0);

                float3 ro_on_bound_LS = ro_LS + rd_LS * tnear_LS;

                MarchResult march_result_LS = rayMarch(ro_on_bound_LS, rd_LS, tfar_LS - tnear_LS);
                clip(march_result_LS.dist);
                float depth_LS = march_result_LS.dist + tnear_LS;

                float3 pos_LS = ro_LS + rd_LS * depth_LS;
                float3 pos_WS = mul(unity_ObjectToWorld, float4(pos_LS, 1)).xyz;
                float4 pos_CS = TransformWorldToHClip(pos_WS);

                float depth_NDC = pos_CS.z / pos_CS.w;
                float3 normal_WS = normalize(mul(unity_ObjectToWorld, float4(march_result_LS.normal, 0)).xyz);
                o.color = _Color * shade(normal_WS);
                o.depth = depth_NDC;
                
                return o;
            }

            FragOutput frag_d1(Varyings i)
            {
                FragOutput o;
                
                static float3 ro_WS = GetCameraPositionWS();
                static float3 objectScale = _Dimensions * _VoxelSize;
                
                static float3 ro_LS = mul(unity_WorldToObject, float4(ro_WS, 1)).xyz;

                float3 rd_WS = normalize(i.world - ro_WS);
                float3 rd_LS = normalize(mul(unity_WorldToObject, float4(rd_WS, 0)).xyz);

                float tnear_LS;
                float tfar_LS;
                float t = _DebugIsAuto > 0 ? 0.5 + 0.5 * sin(_Time.y) : 1;
                // float t = 0.8;
                float3 box_max = objectScale * float3(_DebugX * t, _DebugY * t, _DebugZ * t);
                intersect(ro_LS, rd_LS, 0, box_max, tnear_LS, tfar_LS);
                tnear_LS = max(tnear_LS, 0);

                float3 ro_on_bound_LS = ro_LS + rd_LS * tnear_LS;

                if (any(ro_on_bound_LS > box_max + 0.0001) || any(ro_on_bound_LS < -0.0001))
                {
                    discard;
                }

                float d = sdf(ro_on_bound_LS);
                const float interval = 0.02;
                bool isRed = abs(d % interval) < interval / 2;
                // red green change
                float3 color = isRed ? _DebugColor1 : _DebugColor2;
                if (d < _ZeroFace) color = isRed ? 1 : 0;
                // color *= abs(d) * 10 / _DebugMaxDist;
                o.color = float4(color, 1);

                float3 pos_LS = ro_LS + rd_LS * tnear_LS;
                float3 pos_WS = mul(unity_ObjectToWorld, float4(pos_LS, 1)).xyz;
                float4 pos_CS = TransformWorldToHClip(pos_WS);
                float depth_NDC = pos_CS.z / pos_CS.w;
                o.depth = depth_NDC;
                // o.color = float4((float3)(d), 1);
                return o;
            }
            
            ENDHLSL
        }
    }
}