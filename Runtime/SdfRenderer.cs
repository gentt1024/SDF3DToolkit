using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTT.SDFTK
{
    public static class SdfRenderer
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int VoxelSize = Shader.PropertyToID("_VoxelSize");
        private static readonly int Dimensions = Shader.PropertyToID("_Dimensions");
        private static readonly int Color = Shader.PropertyToID("_Color");
        private static readonly int ZeroFace = Shader.PropertyToID("_ZeroFace");
        private static readonly int ZTestMode = Shader.PropertyToID("_ZTestMode");
        
        private const string RAY_MARCH_SDF_URP = "RayMarchSDF_URP";
        private const string UNIT_01_CUBE = "Unit01Cube"; 

        public static readonly Lazy<Shader> SdfShader = new(() => 
            Resources.Load<Shader>(RAY_MARCH_SDF_URP));
        public static Mesh UnitCubeMesh;
        
        private static Material _sdfMaterial;
        
        public static void RenderSdf(SdfNode sdfNode, Color color, bool isAlwaysRender = false, float zeroFace = 0f)
        {
            if (_sdfMaterial == null)
            {
                _sdfMaterial = new Material(SdfShader.Value);
            }
            RenderSdf(sdfNode, _sdfMaterial, color, isAlwaysRender, zeroFace);
        }
        
        public static void RenderSdf(SdfNode sdfNode, Material sdfMat, Color color, bool isAlwaysRender = false, float zeroFace = 0f)
        {
            if (sdfNode == null)
            {
                Debug.LogError("SdfRenderer: sdfNode is null");
                return;
            }
            sdfMat.SetTexture(MainTex, sdfNode.DistanceField);
            sdfMat.SetFloat(VoxelSize, sdfNode.VoxelSize);
            sdfMat.SetVector(Dimensions, sdfNode.Dimensions);
            sdfMat.SetColor(Color, color);
            sdfMat.SetFloat(ZeroFace, zeroFace);
            // set _ZTestMode
            sdfMat.SetFloat(ZTestMode, isAlwaysRender ? 0 : 4);
            sdfMat.SetPass(0);

            if (UnitCubeMesh == null)
            {
                UnitCubeMesh = GenUnit01Cube();
            }
            Graphics.DrawMeshNow(UnitCubeMesh, sdfNode.Matrix);
        }

        /// <summary>
        /// Generate Cube Mesh from (0, 0, 0) to (1, 1, 1)
        /// </summary>
        private static Mesh GenUnit01Cube()
        {
            Mesh mesh = new Mesh();
            mesh.SetVertices(new List<Vector3>
            {
                new(0, 0, 0),
                new(1, 0, 0),
                new(1, 1, 0),
                new(0, 1, 0),
                new(0, 0, 1),
                new(1, 0, 1),
                new(1, 1, 1),
                new(0, 1, 1),
            });
            mesh.SetIndices(new int[]
            {
                0, 3, 2, 1,
                1, 2, 6, 5,
                5, 6, 7, 4,
                4, 7, 3, 0,
                0, 1, 5, 4,
                3, 7, 6, 2
            }, MeshTopology.Quads, 0);
            return mesh;
        }
    }
}