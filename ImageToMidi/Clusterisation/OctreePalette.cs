using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    /// <summary>
    /// 八叉树调色板聚类算法
    /// </summary>
    public static class OctreePalette
    {
        /// <summary>
        /// 基于八叉树的调色板聚类
        /// </summary>
        /// <param name="image">BGRA字节数组</param>
        /// <param name="colorCount">目标颜色数</param>
        /// <param name="maxLevel">八叉树最大层级</param>
        /// <param name="maxSamples">最大采样像素数</param>
        /// <returns>BitmapPalette</returns>
        public static BitmapPalette CreatePalette(byte[] image, int colorCount, int maxLevel = 8, int maxSamples = 20000)
        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            // 分层管理叶节点
            List<OctreeNode>[] levelLeaves = new List<OctreeNode>[maxLevel + 1];
            for (int i = 0; i <= maxLevel; i++) levelLeaves[i] = new List<OctreeNode>();

            var root = new OctreeNode { Level = 0 };
            // 插入像素到八叉树
            foreach (int idx in sampleIndices)
            {
                InsertColorFast(root, image[idx + 2], image[idx + 1], image[idx + 0], 0, levelLeaves, maxLevel);
            }

            int totalLeaves = levelLeaves.Sum(l => l.Count);
            // 合并叶节点直到数量等于 colorCount
            while (totalLeaves > colorCount)
            {
                int level = maxLevel;
                while (level > 0 && levelLeaves[level].Count == 0) level--;
                if (level == 0) break;
                var node = levelLeaves[level][0];
                if (node.Parent != null && node.IsLeaf)
                {
                    MergeNodeFast(node, levelLeaves);
                    totalLeaves--;
                }
                else
                {
                    levelLeaves[level].RemoveAt(0);
                }
            }

            // 生成调色板
            var palette = new List<Color>();
            for (int level = 0; level <= maxLevel; level++)
            {
                foreach (var node in levelLeaves[level])
                {
                    if (node.PixelCount == 0) continue;
                    palette.Add(Color.FromRgb(
                        (byte)(node.Red / node.PixelCount),
                        (byte)(node.Green / node.PixelCount),
                        (byte)(node.Blue / node.PixelCount)
                    ));
                }
            }
            if (palette.Count == 0)
                palette.Add(Color.FromRgb(0, 0, 0));
            else if (palette.Count > colorCount)
                palette = palette.Take(colorCount).ToList();
            else if (palette.Count < colorCount)
            {
                var fillColor = palette.Count > 0 ? palette[0] : Color.FromRgb(0, 0, 0);
                while (palette.Count < colorCount)
                    palette.Add(fillColor);
            }
            return new BitmapPalette(palette);
        }

        // 优化后的插入
        private static void InsertColorFast(OctreeNode node, int r, int g, int b, int level, List<OctreeNode>[] levelLeaves, int maxLevel)
        {
            if (level == maxLevel)
            {
                node.IsLeaf = true;
                node.PixelCount++;
                node.Red += r;
                node.Green += g;
                node.Blue += b;
                if (!node.AddedToLeaves)
                {
                    levelLeaves[level].Add(node);
                    node.AddedToLeaves = true;
                }
                return;
            }
            int idx = (((r >> (7 - level)) & 1) << 2) | (((g >> (7 - level)) & 1) << 1) | ((b >> (7 - level)) & 1);
            if (node.Children[idx] == null)
            {
                node.Children[idx] = new OctreeNode { Parent = node, Level = level + 1 };
            }
            InsertColorFast(node.Children[idx], r, g, b, level + 1, levelLeaves, maxLevel);
        }

        // 优化后的合并
        private static void MergeNodeFast(OctreeNode node, List<OctreeNode>[] levelLeaves)
        {
            if (!node.IsLeaf) return;
            var parent = node.Parent;
            if (parent == null) return;
            parent.Red += node.Red;
            parent.Green += node.Green;
            parent.Blue += node.Blue;
            parent.PixelCount += node.PixelCount;
            levelLeaves[node.Level].Remove(node);
            node.IsLeaf = false;
            bool allNull = true;
            foreach (var child in parent.Children)
            {
                if (child != null && child.IsLeaf) { allNull = false; break; }
            }
            if (allNull && !parent.AddedToLeaves)
            {
                parent.IsLeaf = true;
                levelLeaves[parent.Level].Add(parent);
                parent.AddedToLeaves = true;
            }
        }

        /// <summary>
        /// Octree节点定义
        /// </summary>
        private class OctreeNode
        {
            public int PixelCount;
            public int Red, Green, Blue;
            public OctreeNode[] Children = new OctreeNode[8];
            public bool IsLeaf;
            //public int PaletteIndex;
            public OctreeNode Parent;
            public int Level;
            public bool AddedToLeaves;
        }
    }
}
