#ifndef VRMC_MATERIALS_MTOON_FORWARD_VERTEX_INCLUDED
#define VRMC_MATERIALS_MTOON_FORWARD_VERTEX_INCLUDED

#include "./vrmc_materials_mtoon_render_pipeline.hlsl"
#include "./vrmc_materials_mtoon_define.hlsl"
#include "./vrmc_materials_mtoon_utility.hlsl"
#include "./vrmc_materials_mtoon_input.hlsl"
#include "./vrmc_materials_mtoon_attribute.hlsl"
#include "./vrmc_materials_mtoon_geometry_vertex.hlsl"
#include "./vrmxt_materials_mtoonxt_overlay_depth.hlsl"

Varyings MToonVertex(const Attributes v) // v is UnityCG macro specified name.
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(MTOONXT_SKIP_THIS_OVERLAY_DEPTH_PASS)
    return output;
#endif

    output.uv = v.texcoord0;

    if (MToon_IsOutlinePass())
    {
        half3 outlineNormal = normalize(v.normalOS);
#if defined(MTOON_PASS_OUTLINE)
        if (_VRMXTGeometricOutline > 0.5 && dot(v.outlineDirectionTS, v.outlineDirectionTS) > 0.01)
        {
            float3 tangent = v.tangentOS.xyz - outlineNormal * dot(v.tangentOS.xyz, outlineNormal);
            tangent *= rsqrt(max(dot(tangent, tangent), 1e-10));
            float3 bitangent = cross(outlineNormal, tangent);
            outlineNormal = normalize(tangent * v.outlineDirectionTS.x + bitangent * v.outlineDirectionTS.y
                + outlineNormal * v.outlineDirectionTS.z);
        }
#endif
        const VertexPositionInfo position = MToon_GetOutlineVertex(v.vertex.xyz, outlineNormal, output.uv);
        output.pos = position.positionCS;
        output.positionWS = position.positionWS.xyz;

        output.normalWS = MToon_TransformObjectToWorldNormal(-outlineNormal);
    }
    else
    {
        const VertexPositionInfo position = MToon_GetVertex(v.vertex.xyz);
        output.pos = position.positionCS;
        output.positionWS = position.positionWS.xyz;

        output.normalWS = MToon_TransformObjectToWorldNormal(v.normalOS);
    }

    output.viewDirWS = MToon_GetWorldSpaceNormalizedViewDir(output.positionWS);

#if defined(_NORMALMAP)
    const half tangentSign = v.tangentOS.w * unity_WorldTransformParams.w;
    output.tangentWS = half4(MToon_TransformObjectToWorldDir(v.tangentOS), tangentSign);
#endif

    MTOON_TRANSFER_FOG_AND_LIGHTING(output, output.pos, v.texcoord1.xy, v.vertex.xyz);

    return output;
}

#endif