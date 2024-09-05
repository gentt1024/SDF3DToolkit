#define SEMICOLON ;
#define COMMA ,

#define SDF_NODE_MACRO(prefix, separator) \
RWTexture3D<float> prefix separator \
float prefix##_voxel_size separator \
uint3 prefix##_dimensions separator \
float4x4 prefix##_mat separator \
float4x4 prefix##_inv_mat

#define SDF_NODE_DEFS(prefix) SDF_NODE_MACRO(prefix, SEMICOLON)

#define SDF_NODE_ARGS(prefix) \
const RWTexture3D<float> prefix, \
const float prefix##_voxel_size, \
const uint3 prefix##_dimensions, \
const float4x4 prefix##_mat, \
const float4x4 prefix##_inv_mat

#define SDF_NODE_VARS(prefix) prefix, prefix##_voxel_size, prefix##_dimensions, prefix##_mat, prefix##_inv_mat

#define SDF_SAMPLE_EPS 1e-3

bool is_inside(uint3 id, uint3 dimensions)
{
    return id.x < dimensions.x && id.y < dimensions.y && id.z < dimensions.z;
}

bool is_inside(float3 id, uint3 dimensions)
{
    return id.x >= 0 && id.y >= 0 && id.z >= 0 &&
        id.x < dimensions.x && id.y < dimensions.y && id.z < dimensions.z;
}

float sample_sdf(SDF_NODE_ARGS(sdf), float3 world_pos)
{
    const float3 ids_sdf_space = (mul(sdf_inv_mat, float4(world_pos, 1)).xyz / sdf_voxel_size);
    // Ensure localPos is within the bounds of the sdf texture
    if (is_inside(ids_sdf_space, sdf_dimensions))
    {
        return sdf[ids_sdf_space];
    }
    // return 1;
    {
        const float3 closest_ids = clamp(ids_sdf_space, 0, float3(sdf_dimensions - 1));
        float closest_sdf = sdf[closest_ids];
        // If the closest distance on boundary is negative, we make a shell to cover the boundary
        if (closest_sdf < 0)
            closest_sdf = 0;
        const uint3 ids_diff = abs(ids_sdf_space - closest_ids);
        const float diff = sqrt(dot(ids_diff, ids_diff)) * sdf_voxel_size * 1.0f;
        return closest_sdf + diff;        
    }
}

float3 sample_sdf_gradient(SDF_NODE_ARGS(sdf), float3 world_pos)
{
    float3 grad = float3(0, 0, 0);
    for (int i = 0; i < 3; i++)
    {
        float3 pos_plus = world_pos;
        pos_plus[i] += SDF_SAMPLE_EPS;
        float3 pos_minus = world_pos;
        pos_minus[i] -= SDF_SAMPLE_EPS;
        const float sdf_plus = sample_sdf(SDF_NODE_VARS(sdf), pos_plus);
        const float sdf_minus = sample_sdf(SDF_NODE_VARS(sdf), pos_minus);
        grad[i] = (sdf_plus - sdf_minus) / (2 * SDF_SAMPLE_EPS);
    }
    return normalize(grad);
}