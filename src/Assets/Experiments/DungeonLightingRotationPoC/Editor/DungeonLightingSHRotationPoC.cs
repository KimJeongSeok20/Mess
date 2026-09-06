using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds an SH L2 rotation matrix from Unity's own SphericalHarmonicsL2 basis,
/// applies it to existing tile probes, and compares the result with an independently
/// baked rotated tile. All generated files stay inside the PoC Evidence folder.
/// </summary>
public static class DungeonLightingSHRotationPoC
{
    private const string Root = "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/BakedData";
    private const string EvidenceFolder = "Assets/Experiments/DungeonLightingRotationPoC/Evidence/SH";
    private const int CoefficientCount = 9;

    [Serializable]
    private sealed class Report
    {
        public bool completed;
        public string failure;
        public string tile;
        public int rotation;
        public string power;
        public int probeCount;
        public int validationSampleCount;
        public float transformMeanAbsoluteResidual;
        public float transformMaxAbsoluteResidual;
        public float targetMeanAbsoluteError;
        public float targetMeanRelativeError;
        public int worstProbeIndex;
        public float worstProbeMeanAbsoluteError;
        public float worstProbeMeanRelativeError;
        public float visualizationExposure;
        public string referenceImage;
        public string candidateImage;
        public string amplifiedDifferenceImage;
    }

    private sealed class ErrorAccumulator
    {
        public int count;
        public double absoluteSum;
        public double relativeSum;
        public float maxAbsolute;

        public void Add(Vector3 value, Vector3 reference)
        {
            float absolute = Vector3.Distance(value, reference);
            float relative = absolute / (value.magnitude + reference.magnitude + 1e-4f);
            count++;
            absoluteSum += absolute;
            relativeSum += relative;
            maxAbsolute = Mathf.Max(maxAbsolute, absolute);
        }

        public float MeanAbsolute => count > 0 ? (float)(absoluteSum / count) : 0f;
        public float MeanRelative => count > 0 ? (float)(relativeSum / count) : 0f;
    }

    [MenuItem("Tools/Dungeon Lighting/Rotation PoC/SH Visual - StartRoom R270 P100")]
    public static void StartRoomMenu()
    {
        Debug.Log(RunCli("StartRoom", 270, "P100"));
    }

    public static string RunCli(string tile, int rotation, string power)
    {
        var report = new Report { tile = tile, rotation = rotation, power = power };
        try
        {
            DungeonTileBakeData source = LoadData(tile, 0, power);
            DungeonTileBakeData target = LoadData(tile, rotation, power);
            DungeonTileBakeData.LightProbeBakeEntry[] sourceEntries = source.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
            DungeonTileBakeData.LightProbeBakeEntry[] targetEntries = target.lightProbeEntries ?? Array.Empty<DungeonTileBakeData.LightProbeBakeEntry>();
            if (sourceEntries.Length != targetEntries.Length || sourceEntries.Length == 0)
                throw new InvalidOperationException($"SH probe count mismatch: {sourceEntries.Length} != {targetEntries.Length}");

            double[,] matrix = BuildRotationMatrix(rotation);
            Vector3[] validationDirections = BuildFibonacciDirections(96);
            Vector3[] inverseDirections = RotateDirections(validationDirections, -rotation);
            var transformError = new ErrorAccumulator();
            var targetError = new ErrorAccumulator();
            int worstProbeIndex = 0;
            float worstProbeRelative = -1f;
            float worstProbeAbsolute = 0f;
            var derived = new SphericalHarmonicsL2[sourceEntries.Length];
            var expectedColors = new Color[validationDirections.Length];
            var derivedColors = new Color[validationDirections.Length];
            var targetColors = new Color[validationDirections.Length];

            for (int probeIndex = 0; probeIndex < sourceEntries.Length; probeIndex++)
            {
                SphericalHarmonicsL2 sourceSh = sourceEntries[probeIndex].ToSphericalHarmonics();
                SphericalHarmonicsL2 targetSh = targetEntries[probeIndex].ToSphericalHarmonics();
                SphericalHarmonicsL2 derivedSh = RotateSh(sourceSh, matrix);
                derived[probeIndex] = derivedSh;

                sourceSh.Evaluate(inverseDirections, expectedColors);
                derivedSh.Evaluate(validationDirections, derivedColors);
                targetSh.Evaluate(validationDirections, targetColors);
                var perProbe = new ErrorAccumulator();
                for (int directionIndex = 0; directionIndex < validationDirections.Length; directionIndex++)
                {
                    Vector3 expected = ToRgb(expectedColors[directionIndex]);
                    Vector3 candidate = ToRgb(derivedColors[directionIndex]);
                    Vector3 reference = ToRgb(targetColors[directionIndex]);
                    transformError.Add(candidate, expected);
                    targetError.Add(candidate, reference);
                    perProbe.Add(candidate, reference);
                }

                if (perProbe.MeanRelative > worstProbeRelative)
                {
                    worstProbeRelative = perProbe.MeanRelative;
                    worstProbeAbsolute = perProbe.MeanAbsolute;
                    worstProbeIndex = probeIndex;
                }
            }

            report.probeCount = sourceEntries.Length;
            report.validationSampleCount = transformError.count;
            report.transformMeanAbsoluteResidual = transformError.MeanAbsolute;
            report.transformMaxAbsoluteResidual = transformError.maxAbsolute;
            report.targetMeanAbsoluteError = targetError.MeanAbsolute;
            report.targetMeanRelativeError = targetError.MeanRelative;
            report.worstProbeIndex = worstProbeIndex;
            report.worstProbeMeanAbsoluteError = worstProbeAbsolute;
            report.worstProbeMeanRelativeError = worstProbeRelative;
            BuildVisualization(
                tile,
                rotation,
                power,
                targetEntries[worstProbeIndex].ToSphericalHarmonics(),
                derived[worstProbeIndex],
                report);
            report.completed = true;
        }
        catch (Exception exception)
        {
            report.completed = false;
            report.failure = exception.ToString();
        }

        string json = JsonUtility.ToJson(report, true);
        string reportPath = Path.GetFullPath($"{EvidenceFolder}/{tile}_R{rotation:000}_{power}_SHReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? Path.GetFullPath(EvidenceFolder));
        File.WriteAllText(reportPath, json);
        Debug.Log($"[DungeonLightingSHRotationPoC]\n{json}");
        return json;
    }

    private static double[,] BuildRotationMatrix(float rotationDegrees)
    {
        Vector3[] fitDirections = BuildFibonacciDirections(128);
        Vector3[] inverseDirections = RotateDirections(fitDirections, -rotationDegrees);
        var basisAtDirections = new double[fitDirections.Length, CoefficientCount];

        for (int basisIndex = 0; basisIndex < CoefficientCount; basisIndex++)
        {
            SphericalHarmonicsL2 basis = BuildBasis(basisIndex);
            var colors = new Color[fitDirections.Length];
            basis.Evaluate(fitDirections, colors);
            for (int sample = 0; sample < fitDirections.Length; sample++)
                basisAtDirections[sample, basisIndex] = colors[sample].r;
        }

        var normal = new double[CoefficientCount, CoefficientCount];
        for (int row = 0; row < CoefficientCount; row++)
        {
            for (int column = 0; column < CoefficientCount; column++)
            {
                double sum = 0d;
                for (int sample = 0; sample < fitDirections.Length; sample++)
                    sum += basisAtDirections[sample, row] * basisAtDirections[sample, column];
                normal[row, column] = sum;
            }
            normal[row, row] += 1e-10d;
        }

        var matrix = new double[CoefficientCount, CoefficientCount];
        for (int sourceBasisIndex = 0; sourceBasisIndex < CoefficientCount; sourceBasisIndex++)
        {
            SphericalHarmonicsL2 sourceBasis = BuildBasis(sourceBasisIndex);
            var desiredColors = new Color[inverseDirections.Length];
            sourceBasis.Evaluate(inverseDirections, desiredColors);
            var rightHandSide = new double[CoefficientCount];
            for (int row = 0; row < CoefficientCount; row++)
            {
                double sum = 0d;
                for (int sample = 0; sample < fitDirections.Length; sample++)
                    sum += basisAtDirections[sample, row] * desiredColors[sample].r;
                rightHandSide[row] = sum;
            }

            double[] solution = SolveLinearSystem(normal, rightHandSide);
            for (int outputCoefficient = 0; outputCoefficient < CoefficientCount; outputCoefficient++)
                matrix[outputCoefficient, sourceBasisIndex] = solution[outputCoefficient];
        }
        return matrix;
    }

    private static SphericalHarmonicsL2 BuildBasis(int coefficientIndex)
    {
        var basis = new SphericalHarmonicsL2();
        basis[0, coefficientIndex] = 1f;
        return basis;
    }

    private static SphericalHarmonicsL2 RotateSh(SphericalHarmonicsL2 source, double[,] matrix)
    {
        var result = new SphericalHarmonicsL2();
        for (int channel = 0; channel < 3; channel++)
        {
            for (int output = 0; output < CoefficientCount; output++)
            {
                double sum = 0d;
                for (int input = 0; input < CoefficientCount; input++)
                    sum += matrix[output, input] * source[channel, input];
                result[channel, output] = (float)sum;
            }
        }
        return result;
    }

    private static double[] SolveLinearSystem(double[,] matrix, double[] vector)
    {
        int size = vector.Length;
        var augmented = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
                augmented[row, column] = matrix[row, column];
            augmented[row, size] = vector[row];
        }

        for (int pivot = 0; pivot < size; pivot++)
        {
            int bestRow = pivot;
            double bestValue = Math.Abs(augmented[pivot, pivot]);
            for (int row = pivot + 1; row < size; row++)
            {
                double value = Math.Abs(augmented[row, pivot]);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = row;
                }
            }
            if (bestValue < 1e-12d)
                throw new InvalidOperationException("SH rotation matrix fit is singular.");

            if (bestRow != pivot)
            {
                for (int column = pivot; column <= size; column++)
                {
                    double temporary = augmented[pivot, column];
                    augmented[pivot, column] = augmented[bestRow, column];
                    augmented[bestRow, column] = temporary;
                }
            }

            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= size; column++)
                augmented[pivot, column] /= divisor;

            for (int row = 0; row < size; row++)
            {
                if (row == pivot)
                    continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column <= size; column++)
                    augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        var solution = new double[size];
        for (int row = 0; row < size; row++)
            solution[row] = augmented[row, size];
        return solution;
    }

    private static void BuildVisualization(
        string tile,
        int rotation,
        string power,
        SphericalHarmonicsL2 reference,
        SphericalHarmonicsL2 candidate,
        Report report)
    {
        const int width = 512;
        const int height = 256;
        var directions = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        {
            float latitude = ((y + 0.5f) / height - 0.5f) * Mathf.PI;
            float cosLatitude = Mathf.Cos(latitude);
            for (int x = 0; x < width; x++)
            {
                float longitude = ((x + 0.5f) / width - 0.5f) * Mathf.PI * 2f;
                directions[y * width + x] = new Vector3(
                    Mathf.Sin(longitude) * cosLatitude,
                    Mathf.Sin(latitude),
                    Mathf.Cos(longitude) * cosLatitude);
            }
        }

        var referenceColors = new Color[directions.Length];
        var candidateColors = new Color[directions.Length];
        reference.Evaluate(directions, referenceColors);
        candidate.Evaluate(directions, candidateColors);
        var magnitudes = new List<float>(directions.Length * 2);
        for (int i = 0; i < directions.Length; i++)
        {
            magnitudes.Add(MaxComponent(referenceColors[i]));
            magnitudes.Add(MaxComponent(candidateColors[i]));
        }
        magnitudes.Sort();
        float percentile95 = magnitudes[Mathf.Clamp(Mathf.FloorToInt(magnitudes.Count * 0.95f), 0, magnitudes.Count - 1)];
        float exposure = percentile95 > 1e-6f ? Mathf.Clamp(0.8f / percentile95, 0.25f, 10000f) : 1f;
        report.visualizationExposure = exposure;

        Texture2D referenceImage = BuildImage(width, height, referenceColors, exposure, null);
        Texture2D candidateImage = BuildImage(width, height, candidateColors, exposure, null);
        Texture2D differenceImage = BuildImage(width, height, referenceColors, exposure * 4f, candidateColors);
        try
        {
            string folder = Path.GetFullPath(EvidenceFolder);
            Directory.CreateDirectory(folder);
            string stem = $"{tile}_R{rotation:000}_{power}_Probe{report.worstProbeIndex:000}";
            string referencePath = Path.Combine(folder, stem + "_reference.png");
            string candidatePath = Path.Combine(folder, stem + "_candidate.png");
            string differencePath = Path.Combine(folder, stem + "_diff_x4.png");
            File.WriteAllBytes(referencePath, referenceImage.EncodeToPNG());
            File.WriteAllBytes(candidatePath, candidateImage.EncodeToPNG());
            File.WriteAllBytes(differencePath, differenceImage.EncodeToPNG());
            report.referenceImage = ToAssetPath(referencePath);
            report.candidateImage = ToAssetPath(candidatePath);
            report.amplifiedDifferenceImage = ToAssetPath(differencePath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(referenceImage);
            UnityEngine.Object.DestroyImmediate(candidateImage);
            UnityEngine.Object.DestroyImmediate(differenceImage);
        }
    }

    private static Texture2D BuildImage(
        int width,
        int height,
        Color[] source,
        float exposure,
        Color[] subtract)
    {
        var pixels = new Color32[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            Color value = subtract == null
                ? source[i]
                : new Color(
                    Mathf.Abs(source[i].r - subtract[i].r),
                    Mathf.Abs(source[i].g - subtract[i].g),
                    Mathf.Abs(source[i].b - subtract[i].b),
                    1f);
            float r = ToneMap(value.r, exposure);
            float g = ToneMap(value.g, exposure);
            float b = ToneMap(value.b, exposure);
            pixels[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                255);
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static float ToneMap(float value, float exposure)
    {
        float mapped = 1f - Mathf.Exp(-Mathf.Max(0f, value) * exposure);
        return Mathf.LinearToGammaSpace(mapped);
    }

    private static float MaxComponent(Color color)
    {
        return Mathf.Max(color.r, Mathf.Max(color.g, color.b));
    }

    private static DungeonTileBakeData LoadData(string tile, int rotation, string power)
    {
        string variant = $"{tile}_R{rotation:000}";
        string path = $"{Root}/{variant}/{power}/{variant}_BakeData.asset";
        DungeonTileBakeData data = AssetDatabase.LoadAssetAtPath<DungeonTileBakeData>(path);
        if (data == null)
            throw new InvalidOperationException($"Could not load {path}");
        return data;
    }

    private static Vector3[] BuildFibonacciDirections(int count)
    {
        var directions = new Vector3[count];
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < count; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / count;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = goldenAngle * i;
            directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return directions;
    }

    private static Vector3[] RotateDirections(Vector3[] source, float degrees)
    {
        Quaternion rotation = Quaternion.Euler(0f, degrees, 0f);
        var result = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
            result[i] = rotation * source[i];
        return result;
    }

    private static Vector3 ToRgb(Color color)
    {
        return new Vector3(color.r, color.g, color.b);
    }

    private static string ToAssetPath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        string project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(project.Length + 1)
            : normalized;
    }
}
