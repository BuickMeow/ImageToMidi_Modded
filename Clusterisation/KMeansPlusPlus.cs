using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;

namespace ImageToMidi
{
    public static partial class Clusterisation
    {
        /// <summary>
        /// KMeans++ ��ʼ����֧�ֽ��Ȼص������²����Ż�
        /// </summary>
        /// <param name="image">BGRA��������</param>
        /// <param name="clusterCount">������</param>
        /// <param name="maxSamples">��������</param>
        /// <param name="seed">�������</param>
        /// <param name="progress">���Ȼص�(0~1)</param>
        /// <returns>BitmapPalette</returns>
        public static BitmapPalette KMeansPlusPlusInit(
            byte[] image,
            int clusterCount,
            int maxSamples = 20000,
            int seed = 0,
            Action<double> progress = null)
        {
            // ������������
            List<int> sampleIndices = SamplePixelIndices(image, maxSamples, seed);
            if (sampleIndices.Count == 0)
                throw new Exception("û�п��õĲ�������");

            Random rand = new Random(seed); // �̶����ӱ�֤�ɸ���
            List<double[]> centers = new List<double[]>();

            // 1. ���ѡ��һ������
            int firstIdx = sampleIndices[rand.Next(sampleIndices.Count)];
            centers.Add(new double[] { image[firstIdx + 2], image[firstIdx + 1], image[firstIdx + 0] });

            double[] minDistSq = new double[sampleIndices.Count];

            // ��ʼ�����е㵽��һ�����ĵľ��루���У�
            Parallel.For(0, sampleIndices.Count, i =>
            {
                int idx = sampleIndices[i];
                double r = image[idx + 2];
                double g = image[idx + 1];
                double b = image[idx + 0];
                double dr = r - centers[0][0];
                double dg = g - centers[0][1];
                double db = b - centers[0][2];
                minDistSq[i] = dr * dr + dg * dg + db * db;
            });

            for (int k = 1; k < clusterCount; k++)
            {
                // �����ܾ��루���й�Լ��
                double total = 0;
                object totalLock = new object();
                Parallel.ForEach(
                    Partitioner.Create(0, minDistSq.Length),
                    () => 0.0,
                    (range, state, localSum) =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                            localSum += minDistSq[i];
                        return localSum;
                    },
                    localSum =>
                    {
                        lock (totalLock) { total += localSum; }
                    });

                // ������ƽ����Ȩ���ѡһ��������
                double pick = rand.NextDouble() * total;
                double acc = 0;
                int chosen = 0;
                for (int i = 0; i < minDistSq.Length; i++)
                {
                    acc += minDistSq[i];
                    if (acc >= pick)
                    {
                        chosen = i;
                        break;
                    }
                }
                int idx2 = sampleIndices[chosen];
                var newCenter = new double[] { image[idx2 + 2], image[idx2 + 1], image[idx2 + 0] };
                centers.Add(newCenter);

                // ���и������е㵽�����ĵ���С����
                Parallel.For(0, sampleIndices.Count, i =>
                {
                    int idx = sampleIndices[i];
                    double r = image[idx + 2];
                    double g = image[idx + 1];
                    double b = image[idx + 0];
                    double dr = r - newCenter[0];
                    double dg = g - newCenter[1];
                    double db = b - newCenter[2];
                    double dist = dr * dr + dg * dg + db * db;
                    if (dist < minDistSq[i]) minDistSq[i] = dist;
                });

                // ���Ȼص�
                progress?.Invoke((double)(k + 1) / clusterCount);
            }

            // תΪColor�б�
            var colorList = new List<Color>(clusterCount);
            foreach (var c in centers)
                colorList.Add(Color.FromRgb((byte)c[0], (byte)c[1], (byte)c[2]));
            return new BitmapPalette(colorList);
        }
    }
}
