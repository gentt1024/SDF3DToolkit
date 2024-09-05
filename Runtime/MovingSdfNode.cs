using System;
using UnityEngine;

namespace GTT.SDFTK
{
    public class MovingSdfNode : IDisposable
    {
        private SdfNode _sdfNode;
        private Matrix4x4 _localMatrix;
            
        public SdfNode SdfNode => _sdfNode;

        public Matrix4x4 Matrix
        {
            get => _sdfNode.Matrix;
            set => _sdfNode.Matrix = value * _localMatrix;
        }
            
        public MovingSdfNode(SdfNode sdfNode)
        {
            _sdfNode = sdfNode;
            _localMatrix = _sdfNode.Matrix;
        }

        public void Dispose()
        {
            _sdfNode.Dispose();
        }
    }
}