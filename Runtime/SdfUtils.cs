using System;
using System.Collections.Generic;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.VFX.SDF;
using Object = UnityEngine.Object;

namespace GTT.SDFTK
{
    public static class SdfUtils
    {
        private static class ShaderProperties
        {
            public static readonly int Mats = Shader.PropertyToID("mats");
            public static readonly int InvMats = Shader.PropertyToID("inv_mats");
            public static readonly int MatsCount = Shader.PropertyToID("mats_count");
            
            public static readonly int Input = Shader.PropertyToID("input");
            public static readonly int Dimensions = Shader.PropertyToID("dimensions");
            public static readonly int Buffer = Shader.PropertyToID("buffer");
            
            public static readonly int Radius = Shader.PropertyToID("radius");
            public static readonly int Center = Shader.PropertyToID("center");
        }

        private const string SDF_NODE_COMBINATIONS = "SdfNodeCombinations";
        private const string SDF_NODE_SWEEP_VOLUME = "SdfNodeSweptVolume";
        private const string COPY_TO_BUFFER = "CopyToBuffer";
        private const string SDF_NODE_SPHERE_COMBINATIONS = "SdfNodeSphereCombinations";

        private static readonly Lazy<ComputeShader> SDFNodeCombinations = new(() =>
            Resources.Load<ComputeShader>(SDF_NODE_COMBINATIONS));

        private static readonly Lazy<ComputeShader> SDFNodeSweptVolume = new(() =>
            Resources.Load<ComputeShader>(SDF_NODE_SWEEP_VOLUME));

        private static readonly Lazy<ComputeShader> SDFCopyToBuffer = new(() =>
            Resources.Load<ComputeShader>(COPY_TO_BUFFER));
        
        private static readonly Lazy<ComputeShader> SDFNodeSphereCombinations = new(() =>
            Resources.Load<ComputeShader>(SDF_NODE_SPHERE_COMBINATIONS));

        private static float[] _dataBuffer1D;
        private static Vector3[] _dataBuffer3D;
        private static readonly int[] Dimensions = new int[3];

        private static RenderTexture CreateSdfRenderTexture(int[] dimensions)
        {
            RenderTexture rt = null;
            CreateRenderTextureIfNeeded(ref rt, new RenderTextureDescriptor
            {
                graphicsFormat = GraphicsFormat.R16_SFloat,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                width = dimensions[0],
                height = dimensions[1],
                volumeDepth = dimensions[2],
                msaaSamples = 1,
            });
            return rt;
        }

        public static SdfNode Union(SdfNode sdfNode1, SdfNode sdfNode2)
        {
            var bounds1 = sdfNode1.GetWorldBounds();
            var bounds2 = sdfNode2.GetWorldBounds();

            var unionBounds = bounds1;
            unionBounds.Encapsulate(bounds2);

            PrepareCombinationData(sdfNode1, sdfNode2, unionBounds, "opUnion", 
                out var resultSdf, out var resultMat);

            return new SdfNode(resultSdf, resultMat, sdfNode1.VoxelSize);
        }
        
        public static void UnionWith(this SdfNode sdfNode1, SdfNode sdfNode2)
        {
            var bounds1 = sdfNode1.GetWorldBounds();
            var bounds2 = sdfNode2.GetWorldBounds();

            var unionBounds = bounds1;
            unionBounds.Encapsulate(bounds2);

            PrepareCombinationData(sdfNode1, sdfNode2, unionBounds, "opUnion", 
                out var resultSdf, out var resultMat);

            sdfNode1.Set(resultSdf, resultMat, sdfNode1.VoxelSize);
        }

        public static bool Intersection(SdfNode sdfNodeA, SdfNode sdfNodeB, float distance, 
            out SdfNode intersection, out int volumeVoxelCount)
        {
            intersection = null;
            volumeVoxelCount = 0;
            if (!sdfNodeA.IntersectsBounds(sdfNodeB, out var intersectBounds))
                return false;

            PrepareCombinationData(sdfNodeA, sdfNodeB, intersectBounds, "opIntersection",
                out var resultSdf, out var resultMat);

            // Judge whether the intersection sdf is empty,
            // that is, whether there is an intersection,
            // that is, whether all values are positive.
            GetData1D(resultSdf, ref _dataBuffer1D, out int length);
            
            for (var i = 0; i < length; i++)
                if (_dataBuffer1D[i] <= distance)
                    volumeVoxelCount++;

            // float voxelSizeInMillimeter = sdfNodeA.VoxelSize * 1000;
            // float volume = volumeVoxelCount * MathF.Pow(voxelSizeInMillimeter, 3);
            // Debug.Log($"Intersection volume: {volume}");
            
            if (volumeVoxelCount > 0)
            {
                intersection = new SdfNode(resultSdf, resultMat, sdfNodeA.VoxelSize);
                return true;
            }
            
            ReleaseRenderTexture(ref resultSdf);
            return false;
        }

        private static void PrepareCombinationData(
            SdfNode sdfNodeA, SdfNode sdfNodeB,
            Bounds resultBounds, string kernelName,
            out RenderTexture resultSdf, out Matrix4x4 resultMat)
        {
            var cs = SDFNodeCombinations.Value;
            var voxelSize = sdfNodeA.VoxelSize;

            var size = resultBounds.size;
            Dimensions[0] = Mathf.CeilToInt(size.x / voxelSize);
            Dimensions[1] = Mathf.CeilToInt(size.y / voxelSize);
            Dimensions[2] = Mathf.CeilToInt(size.z / voxelSize);
            resultMat = Matrix4x4.TRS(resultBounds.min, Quaternion.identity, Vector3.one);

            // create result texture
            resultSdf = CreateSdfRenderTexture(Dimensions);

            // set shader parameters
            var kernelIndex = cs.FindKernel(kernelName);
            cs.SetSdfNode(kernelIndex, "sdf1", sdfNodeA);
            cs.SetSdfNode(kernelIndex, "sdf2", sdfNodeB);
            cs.SetSdfNode(kernelIndex, "result", resultSdf, resultMat, voxelSize);

            // execute shader
            int threadGroupsX = Math.Max(1, Mathf.CeilToInt(Dimensions[0] / 8.0f));
            int threadGroupsY = Math.Max(1, Mathf.CeilToInt(Dimensions[1] / 8.0f));
            int threadGroupsZ = Math.Max(1, Mathf.CeilToInt(Dimensions[2] / 8.0f));
            cs.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        }
        
        public static SdfNode SphereSubtraction(SdfNode sdfNode, Vector3 center, float radius)
        {
            var bounds = sdfNode.GetWorldBounds();
            var sphereBounds = new Bounds(center, Vector3.one * radius * 2);
            if (!bounds.Intersects(sphereBounds))
                return sdfNode.Copy();

            var resultBounds = bounds;
            resultBounds.Encapsulate(sphereBounds);

            var cs = SDFNodeSphereCombinations.Value;
            var voxelSize = sdfNode.VoxelSize;

            var size = resultBounds.size;
            Dimensions[0] = Mathf.CeilToInt(size.x / voxelSize);
            Dimensions[1] = Mathf.CeilToInt(size.y / voxelSize);
            Dimensions[2] = Mathf.CeilToInt(size.z / voxelSize);
            var resultMat = Matrix4x4.TRS(resultBounds.min, Quaternion.identity, Vector3.one);

            // create result texture
            var resultSdf = CreateSdfRenderTexture(Dimensions);

            // set shader parameters
            var kernelIndex = cs.FindKernel("opSubtraction");
            cs.SetSdfNode(kernelIndex, "sdf", sdfNode);
            cs.SetSdfNode(kernelIndex, "result", resultSdf, resultMat, voxelSize);
            cs.SetVector(ShaderProperties.Center, center);
            cs.SetFloat(ShaderProperties.Radius, radius);

            // execute shader
            int threadGroupsX = Math.Max(1, Mathf.CeilToInt(Dimensions[0] / 8.0f));
            int threadGroupsY = Math.Max(1, Mathf.CeilToInt(Dimensions[1] / 8.0f));
            int threadGroupsZ = Math.Max(1, Mathf.CeilToInt(Dimensions[2] / 8.0f));
            cs.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);

            return new SdfNode(resultSdf, resultMat, voxelSize);
        }
        
        public static RenderTexture CopyTexture(RenderTexture src)
        {
            var rtDesc = src.descriptor;
            RenderTexture dst = null;
            CreateRenderTextureIfNeeded(ref dst, rtDesc);
            Graphics.CopyTexture(src, dst);
            
            return dst;
        }

        public static SdfNode BakeSDF(Mesh mesh, float voxelSize, float extent = 0f)
        {
            var bounds = mesh.bounds;
            if (extent != 0)
                bounds.extents += Vector3.one * extent;
            var size = bounds.size;
            float sizeMax = Mathf.Max(size.x, size.y, size.z);
            var center = bounds.center;
            var areaMin = bounds.min;
            var resolution = Mathf.CeilToInt(sizeMax / voxelSize);
            // var signPassesCount = 5;
            // var inOutThreshold = 0.01f;
            // var surfaceOffset = 0.01f;
            using var baker = new MeshToSDFBaker(size, center, resolution, mesh);
            baker.BakeSDF();
            // copy sdf to a new texture because the baker will release the sdf texture
            var newSdf = CopyTexture(baker.SdfTexture);
            var fromSdfToMesh = Matrix4x4.Translate(areaMin);
            // Debug.Log($"Baked SDF: {mesh.name}, voxel size: {voxelSize}, resolution: {resolution}");
            return new SdfNode(newSdf, fromSdfToMesh, voxelSize);
        }
        
        public static SdfNode BakeSDF(List<Mesh> meshes, List<Matrix4x4> transforms, float voxelSize)
        {
            var bounds = meshes[0].bounds;
            for (var i = 1; i < meshes.Count; i++)
            {
                bounds.Encapsulate(meshes[i].bounds);
            }
            var size = bounds.size;
            float sizeMax = Mathf.Max(size.x, size.y, size.z);
            var center = bounds.center;
            var areaMin = bounds.min;
            var resolution = Mathf.CeilToInt(sizeMax / voxelSize);
            using var baker = new MeshToSDFBaker(size, center, resolution, meshes, transforms);
            baker.BakeSDF();
            // copy sdf to a new texture because the baker will release the sdf texture
            var newSdf = CopyTexture(baker.SdfTexture);
            var fromSdfToMesh = Matrix4x4.Translate(areaMin);
            return new SdfNode(newSdf, fromSdfToMesh, voxelSize);
        }

        public static SdfNode SweptVolume(SdfNode sdfNode, List<Matrix4x4> mats)
        {
            var cs = SDFNodeSweptVolume.Value;
            // Calculate the full bound by bound and moves to get the full area, resolution, and voxel size
            var voxelSize = sdfNode.VoxelSize;
            var bounds = sdfNode.GetWorldBounds();
            var fullBound = CalculateFullArea(bounds, mats);
            var areaMin = fullBound.min;
            var resultMat = Matrix4x4.Translate(areaMin);
            var size = fullBound.size;
            Dimensions[0] = Mathf.CeilToInt(size.x / voxelSize);
            Dimensions[1] = Mathf.CeilToInt(size.y / voxelSize);
            Dimensions[2] = Mathf.CeilToInt(size.z / voxelSize);

            // Create a 3D texture to store the result
            var result = CreateSdfRenderTexture(Dimensions);

            // Set shader parameters
            int kernelIndex = cs.FindKernel("SweptVolume");

            // set sdf
            cs.SetSdfNode(kernelIndex, "sdf", sdfNode);
            
            // set swept volume
            cs.SetSdfNode(kernelIndex, "result", result, resultMat, voxelSize);

            // set mats
            using var movesBuffer = new ComputeBuffer(mats.Count, sizeof(float) * 16);
            using var _ = ListPool<Matrix4x4>.Get(out var invMats);
            if (invMats.Capacity < mats.Count) 
                invMats.Capacity = mats.Count;
            foreach (var mat in mats) invMats.Add(mat.inverse);
            movesBuffer.SetData(invMats);
            cs.SetBuffer(kernelIndex, ShaderProperties.InvMats, movesBuffer);
            cs.SetInt(ShaderProperties.MatsCount, mats.Count);

            // Execute shader
            int threadGroupsX = Math.Max(1, Mathf.CeilToInt(Dimensions[0] / 8.0f));
            int threadGroupsY = Math.Max(1, Mathf.CeilToInt(Dimensions[1] / 8.0f));
            int threadGroupsZ = Math.Max(1, Mathf.CeilToInt(Dimensions[2] / 8.0f));
            cs.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);

            return new SdfNode(result, resultMat, voxelSize);
        }

        private static void SetSdfNode(this ComputeShader cs, int kernelIndex, string name,
            RenderTexture sdf, Matrix4x4 matrix, float voxelSize)
        {
            cs.SetTexture(kernelIndex, name, sdf);
            var paramsName = name + "_";
            cs.SetFloat(paramsName + "voxel_size", voxelSize);
            cs.SetVector(paramsName + "dimensions", new Vector3(sdf.width, sdf.height, sdf.volumeDepth));
            cs.SetMatrix(paramsName + "mat", matrix);
            cs.SetMatrix(paramsName + "inv_mat", matrix.inverse);
        }
        
        private static void SetSdfNode(this ComputeShader cs, int kernelIndex, string name, SdfNode sdfNode)
        {
            cs.SetSdfNode(kernelIndex, name, sdfNode.DistanceField, sdfNode.Matrix, sdfNode.VoxelSize);
        }

        private static Bounds CalculateFullArea(Bounds box, List<Matrix4x4> mats)
        {
            var fullBound = box;
            fullBound = Transform(fullBound, mats[0]);

            for (var i = 1; i < mats.Count; i++)
            {
                var mat = mats[i];
                var newBox = box;
                newBox = Transform(newBox, mat);
                fullBound.Encapsulate(newBox);
            }

            return fullBound;
        }

        internal static Bounds Transform(Bounds bounds, Matrix4x4 transformMatrix)
        {
            Vector3 rightAxis = transformMatrix.GetColumn(0);
            Vector3 upAxis = transformMatrix.GetColumn(1);
            Vector3 lookAxis = transformMatrix.GetColumn(2);

            Vector3 rotatedExtentsRight = rightAxis * bounds.extents.x;
            Vector3 rotatedExtentsUp = upAxis * bounds.extents.y;
            Vector3 rotatedExtentsLook = lookAxis * bounds.extents.z;

            float newExtentsX = Mathf.Abs(rotatedExtentsRight.x) + Mathf.Abs(rotatedExtentsUp.x) +
                                Mathf.Abs(rotatedExtentsLook.x);
            float newExtentsY = Mathf.Abs(rotatedExtentsRight.y) + Mathf.Abs(rotatedExtentsUp.y) +
                                Mathf.Abs(rotatedExtentsLook.y);
            float newExtentsZ = Mathf.Abs(rotatedExtentsRight.z) + Mathf.Abs(rotatedExtentsUp.z) +
                                Mathf.Abs(rotatedExtentsLook.z);

            var transformedBounds = new Bounds
            {
                center = transformMatrix.MultiplyPoint(bounds.center),
                extents = new Vector3(newExtentsX, newExtentsY, newExtentsZ)
            };

            return transformedBounds;
        }
        
        private static readonly HashSet<RenderTexture> UnreleasedRenderTextures = new(16);

        static SdfUtils()
        {
            // destroy all render textures when the application quits
            Application.quitting += () =>
            {
                Debug.Log($"Destroying {UnreleasedRenderTextures.Count} unreleased render textures");
                foreach (var rt in UnreleasedRenderTextures)
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                UnreleasedRenderTextures.Clear();
            };
        }
        
        internal static void CreateRenderTextureIfNeeded(ref RenderTexture rt, RenderTextureDescriptor rtDesc)
        {
            if (rt != null
                && rt.width == rtDesc.width
                && rt.height == rtDesc.height
                && rt.volumeDepth == rtDesc.volumeDepth
                && rt.graphicsFormat == rtDesc.graphicsFormat)
            {
                return;
            }
            ReleaseRenderTexture(ref rt);
            rt = new RenderTexture(rtDesc);
            rt.hideFlags = HideFlags.DontSave;
            rt.Create();
            
            UnreleasedRenderTextures.Add(rt);
           
            // Debug.Log($"Created render texture, total unreleased: {UnreleasedRenderTextures.Count}");
        }

        internal static void ReleaseRenderTexture(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                
                UnreleasedRenderTextures.Remove(rt);
                // Debug.Log($"Released render texture, total unreleased: {UnreleasedRenderTextures.Count}");
            }
            rt = null;
        }

        internal static void GetData1D(RenderTexture sdf, ref float[] data, out int length)
        {
            using var buffer = CopyToBuffer(sdf, sizeof(float), out length);

            if (data == null || data.Length < length)
            {
                data = new float[length];
            }
            
            buffer.GetData(data, 0, 0, length);
            buffer.Release();
        }
        
        internal static void GetData3D(RenderTexture sdf, ref Vector3[] data, out int length)
        {
            using var buffer = CopyToBuffer(sdf, sizeof(float) * 3, out length);
            
            if (data == null || data.Length < length)
                data = new Vector3[length];
            
            buffer.GetData(data, 0, 0, length);
            buffer.Release();
        }
        
        private static ComputeBuffer CopyToBuffer(RenderTexture sdf, int stride, out int length)
        {
            length = sdf.width * sdf.height * sdf.volumeDepth;
            var buffer = new ComputeBuffer(length, stride);
            var cs = SDFCopyToBuffer.Value;
            var kernelIndex = cs.FindKernel("CopyToBuffer");
            cs.SetTexture(kernelIndex, ShaderProperties.Input, sdf);
            cs.SetBuffer(kernelIndex, ShaderProperties.Buffer, buffer);
            Dimensions[0] = sdf.width;
            Dimensions[1] = sdf.height;
            Dimensions[2] = sdf.volumeDepth;
            cs.SetInts(ShaderProperties.Dimensions, Dimensions);
            int threadGroupsX = Math.Max(1, Mathf.CeilToInt(Dimensions[0] / 8.0f));
            int threadGroupsY = Math.Max(1, Mathf.CeilToInt(Dimensions[1] / 8.0f));
            int threadGroupsZ = Math.Max(1, Mathf.CeilToInt(Dimensions[2] / 8.0f));
            cs.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
            return buffer;
        }

#if UNITY_EDITOR
        public static void SaveRT3DAsync(RenderTexture rt3D, string dir, string assetName)
        {
            int width = rt3D.width, height = rt3D.height, depth = rt3D.volumeDepth;
            var a = new NativeArray<float>(width * height * depth, Allocator.Persistent,
                NativeArrayOptions
                    .UninitializedMemory); //change if format is not 8 bits (i was using R8_UNorm) (create a struct with 4 bytes etc)
            AsyncGPUReadback.RequestIntoNativeArray(ref a, rt3D, 0, (_) =>
            {
                Texture3D output = new Texture3D(width, height, depth, TextureFormat.RHalf, false);
                output.filterMode = FilterMode.Bilinear;
                output.wrapMode = TextureWrapMode.Clamp;
                output.SetPixelData(a, 0);
                output.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                string path = dir + assetName + ".asset";
                if (path != "")
                {
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(output, path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }

                a.Dispose();
            });
        }

#endif
    }
}