using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Vision.Inference;

/// <summary>推理输出解码：目标检测、分类、分割与 NMS。</summary>
public static class OutputPostProcessor
{
    /// <summary>解码目标检测输出，兼容 [1, C, N]（YOLOv8 转置）与 [1, N, C]（YOLOv5）。</summary>
    public static List<Detection> DecodeDetections(
        Tensor<float> output,
        PreprocessResult preprocess,
        ModelDescriptor descriptor)
    {
        var dims = output.Dimensions.ToArray();
        var channelsFirst = dims.Length == 3 && dims[1] < dims[2];
        var numChannels = dims.Length == 3 ? (channelsFirst ? dims[1] : dims[2]) : dims[^1];
        var numAnchors = dims.Length == 3 ? (channelsFirst ? dims[2] : dims[1]) : 1;

        if (numChannels <= 5)
        {
            return new List<Detection>();
        }

        float At(int anchor, int channel)
            => channelsFirst ? output[0, channel, anchor] : output[0, anchor, channel];

        var labels = descriptor.Labels;
        var detections = new List<Detection>(numAnchors);

        for (var anchor = 0; anchor < numAnchors; anchor++)
        {
            var bestClass = -1;
            var bestScore = 0d;
            for (var channel = 4; channel < numChannels; channel++)
            {
                var score = At(anchor, channel);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = channel - 4;
                }
            }

            if (bestClass < 0 || bestScore < descriptor.Confidence)
            {
                continue;
            }

            var centerX = At(anchor, 0);
            var centerY = At(anchor, 1);
            var width = At(anchor, 2);
            var height = At(anchor, 3);

            if (width <= 1f || height <= 1f)
            {
                continue;
            }

            var box = preprocess.ToOriginal(new FloatRect(centerX - (width / 2d), centerY - (height / 2d), width, height));
            detections.Add(new Detection(
                bestClass,
                LabelOf(labels, bestClass),
                bestScore,
                box));
        }

        return Suppress(detections, descriptor.Nms, descriptor.MaxDetections);
    }

    /// <summary>解码分类输出为概率序列（降序）。</summary>
    public static List<ClassProbability> DecodeClassification(Tensor<float> output, ModelDescriptor descriptor)
    {
        var dims = output.Dimensions.ToArray();
        var count = dims[^1];
        var values = new float[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = Flatten(output, index);
        }

        var max = values.Max();
        var sum = 0d;
        for (var index = 0; index < count; index++)
        {
            sum += Math.Exp(values[index] - max);
        }

        var probabilities = new List<ClassProbability>(count);
        for (var index = 0; index < count; index++)
        {
            probabilities.Add(new ClassProbability(
                index,
                LabelOf(descriptor.Labels, index),
                Math.Exp(values[index] - max) / sum));
        }

        return probabilities
            .OrderByDescending(item => item.Probability)
            .Take(Math.Max(1, descriptor.MaxDetections))
            .ToList();
    }

    private static float Flatten(Tensor<float> tensor, int index)
    {
        // 分类输出一般为 [C] / [1, C] / [1, 1, C]，按维度形状定位。
        var dims = tensor.Dimensions.ToArray();
        return dims.Length switch
        {
            1 => tensor[index],
            2 => tensor[0, index],
            3 => tensor[0, 0, index],
            4 => tensor[0, 0, 0, index],
            _ => 0f
        };
    }

    /// <summary>把分割输出转成与原图同尺寸的二值掩膜。</summary>
    public static Mat CreateMask(Tensor<float> output, PreprocessResult preprocess, Mat original)
    {
        var dims = output.Dimensions.ToArray();
        var height = dims[^2];
        var width = dims[^1];

        var mask = new Mat(height, width, MatType.CV_8UC1);
        var index = mask.GetGenericIndexer<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = dims.Length switch
                {
                    4 => output[0, 0, y, x],
                    3 => output[0, y, x],
                    _ => 0f
                };

                index[y, x] = value > 0.5f ? (byte)255 : (byte)0;
            }
        }

        var resized = new Mat();
        Cv2.Resize(mask, resized, new Size(original.Width, original.Height), 0, 0, InterpolationFlags.Nearest);
        mask.Dispose();
        return resized;
    }

    /// <summary>非极大值抑制，按类别独立执行。</summary>
    public static List<Detection> Suppress(IReadOnlyList<Detection> detections, double iouThreshold, int maxDetections)
    {
        var kept = new List<Detection>();
        foreach (var group in detections.GroupBy(detection => detection.ClassId))
        {
            var candidates = group.OrderByDescending(detection => detection.Confidence).ToList();
            while (candidates.Count > 0 && kept.Count < maxDetections)
            {
                var current = candidates[0];
                kept.Add(current);
                candidates.RemoveAt(0);
                candidates.RemoveAll(candidate => IntersectionOverUnion(current.Box, candidate.Box) > iouThreshold);
            }
        }

        return kept
            .OrderByDescending(detection => detection.Confidence)
            .Take(Math.Max(1, maxDetections))
            .ToList();
    }

    /// <summary>交并比。</summary>
    public static double IntersectionOverUnion(FloatRect a, FloatRect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        var intersection = Math.Max(0d, right - left) * Math.Max(0d, bottom - top);
        var union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;
        return union <= 0 ? 0d : intersection / union;
    }

    private static string LabelOf(IReadOnlyList<string> labels, int classId)
        => classId >= 0 && classId < labels.Count ? labels[classId] : $"class{classId}";
}
