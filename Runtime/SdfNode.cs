using System;
using Unity.Mathematics;
using UnityEngine;

namespace GTT.SDFTK
{
    
    public class SdfNode : IDisposable
    {
        private RenderTexture _distanceField;
        private SdfNodeParams _params;
        private float[] _voxelData;

        public RenderTexture DistanceField
        {
            get => _distanceField;
        }

        public Matrix4x4 Matrix
        {
            get => _params.Matrix;
            set => _params.Matrix = value;
        }
        public float VoxelSize => _params.VoxelSize;
        
        public Vector3 Dimensions => new Vector3(_params.Dimensions.x, _params.Dimensions.y, _params.Dimensions.z);
        public SdfNodeParams Params => _params;
        
        private bool _isDisposed;
        
        public SdfNode(RenderTexture distanceField, Matrix4x4 matrix, float voxelSize)
        {
            Set(distanceField, matrix, voxelSize);
        }
        
        public SdfNode Copy()
        {
            var copySdf = SdfUtils.CopyTexture(_distanceField);
            var copy = new SdfNode(copySdf, Matrix, VoxelSize);
            return copy;
        }
        
        internal void Set(RenderTexture distanceField, Matrix4x4 matrix, float voxelSize)
        {
            SdfUtils.ReleaseRenderTexture(ref _distanceField);
            _voxelData = null;
            
            _distanceField = distanceField;
            _params = new SdfNodeParams
            {
                VoxelSize = voxelSize,
                Dimensions = new uint3((uint)distanceField.width, (uint)distanceField.height, (uint)distanceField.volumeDepth),
                Matrix = matrix
            };
            
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            
            SdfUtils.ReleaseRenderTexture(ref _distanceField);
            _voxelData = null;
            GC.SuppressFinalize(this);
            
            _isDisposed = true;
        }
        
        public Bounds GetLocalBounds()
        {
            var bounds = new Bounds();
            bounds.size = new Vector3(_distanceField.width, _distanceField.height, _distanceField.volumeDepth) * VoxelSize;
            bounds.center = bounds.size / 2;
            return bounds;
        }
        
        public Bounds GetWorldBounds()
        {
            var bounds = GetLocalBounds();
            var finalBounds = SdfUtils.Transform(bounds, Matrix);
            return finalBounds;
        }
        
        public bool IntersectsBounds(SdfNode other, out Bounds intersectionBounds)
        {
            intersectionBounds = new Bounds();
            var bounds = GetWorldBounds();
            
            var otherBounds = other.GetWorldBounds();
            
            if (!bounds.Intersects(otherBounds))
                return false;
            
            var min = Vector3.Max(bounds.min, otherBounds.min);
            var max = Vector3.Min(bounds.max, otherBounds.max);
            intersectionBounds = new Bounds((min + max) / 2, max - min);
            return true;
        }
        
        private int ID3(int x, int y, int z)
        {
            return x + y * _distanceField.width + z * _distanceField.width * _distanceField.height;
        }
        
        public float SD(Vector3 worldPos)
        {
            var localPos = Matrix.inverse.MultiplyPoint(worldPos);
            if (_voxelData == null)
            {
                SdfUtils.GetData1D(_distanceField, ref _voxelData, out var length);
            }
            int x = Mathf.FloorToInt(localPos.x / VoxelSize);
            int y = Mathf.FloorToInt(localPos.y / VoxelSize);
            int z = Mathf.FloorToInt(localPos.z / VoxelSize);
            if (x < 0 || x >= _distanceField.width || y < 0 || y >= _distanceField.height || z < 0 || z >= _distanceField.volumeDepth)
            {
                int cx = Mathf.Clamp(x, 0, _distanceField.width - 1);
                int cy = Mathf.Clamp(y, 0, _distanceField.height - 1);
                int cz = Mathf.Clamp(z, 0, _distanceField.volumeDepth - 1);
                float closestDistance = _voxelData[ID3(cx, cy, cz)];
                if (closestDistance < 0)
                    closestDistance = 0;
                Vector3 idsDiff = new (x - cx, y - cy, z - cz);
                float diff = MathF.Sqrt(Vector3.Dot(idsDiff, idsDiff)) * VoxelSize;
                return closestDistance + diff;
            }
            else
            {
                return _voxelData[ID3(x, y, z)];
            }
        }
        
        public Vector3 Gradient(Vector3 worldPos)
        {
            Vector3 grad = new Vector3();
            float eps = VoxelSize * 1.5f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 posPlus = worldPos;
                posPlus[i] += eps;
                Vector3 posMinus = worldPos;
                posMinus[i] -= eps;
                var sdfPlus = SD(posPlus);
                var sdfMinus = SD(posMinus);
                grad[i] = (sdfPlus - sdfMinus) / (2 * eps);
            }
            return grad.normalized;
        }
    }
}