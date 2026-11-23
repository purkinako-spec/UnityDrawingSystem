using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Layers
{
    /// <summary>
    /// レイヤーの作成・順序管理・アクティブ選択を統括するマネージャー
    /// </summary>
    public class LayerManager : IDisposable
    {
        private readonly List<DrawingLayer> layers = new List<DrawingLayer>();
        private readonly LayerCompositor compositor;
        private readonly int width;
        private readonly int height;
        private int activeLayerIndex = 0;
        private bool isDisposed;

        public event Action OnLayersChanged;

        public IReadOnlyList<DrawingLayer> Layers => layers;
        public int ActiveLayerIndex => activeLayerIndex;

        public DrawingLayer ActiveLayer
        {
            get
            {
                if (activeLayerIndex >= 0 && activeLayerIndex < layers.Count)
                {
                    return layers[activeLayerIndex];
                }
                return null;
            }
        }

        public LayerManager(int width, int height)
        {
            this.width = width;
            this.height = height;
            this.compositor = new LayerCompositor();

            // 初期レイヤー（レイヤー1）を作成
            AddLayer("Layer 1");
        }

        public DrawingLayer AddLayer(string name = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = $"Layer {layers.Count + 1}";
            }

            var newLayer = new DrawingLayer(name, width, height);
            layers.Add(newLayer);
            activeLayerIndex = layers.Count - 1;

            OnLayersChanged?.Invoke();
            return newLayer;
        }

        public bool RemoveLayer(int index)
        {
            if (layers.Count <= 1)
            {
                Debug.LogWarning("[DrawingSystem] Cannot remove the last remaining layer.");
                return false;
            }

            if (index < 0 || index >= layers.Count) return false;

            var layer = layers[index];
            layers.RemoveAt(index);
            layer.Dispose();

            if (activeLayerIndex >= layers.Count)
            {
                activeLayerIndex = layers.Count - 1;
            }

            OnLayersChanged?.Invoke();
            return true;
        }

        public void SetActiveLayer(int index)
        {
            if (index >= 0 && index < layers.Count)
            {
                activeLayerIndex = index;
                OnLayersChanged?.Invoke();
            }
        }

        public void MoveLayer(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= layers.Count || toIndex < 0 || toIndex >= layers.Count) return;

            var item = layers[fromIndex];
            layers.RemoveAt(fromIndex);
            layers.Insert(toIndex, item);

            activeLayerIndex = toIndex;
            OnLayersChanged?.Invoke();
        }

        public void ClearAllLayers()
        {
            foreach (var layer in layers)
            {
                layer.Clear();
            }
            OnLayersChanged?.Invoke();
        }

        /// <summary>
        /// 全レイヤーを指定された出力テクスチャに合成します
        /// </summary>
        public void CompositeTo(RenderTexture destination, Color backgroundColor)
        {
            compositor.Composite(layers.ToArray(), destination, backgroundColor);
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            foreach (var layer in layers)
            {
                layer.Dispose();
            }
            layers.Clear();
            compositor?.Dispose();
        }
    }
}
