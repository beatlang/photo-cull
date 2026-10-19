using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PCull.Core.Abstractions
{
    public enum InferenceProvider
    {
        DirectML,
        CPU
    }

    public interface IInferenceEngine : IDisposable
    {
        InferenceProvider ActiveProvider { get; }

        IReadOnlyList<string> InputNames { get; }
        IReadOnlyList<string> OutputNames { get; }

        IReadOnlyDictionary<string, float[]> Run(IReadOnlyDictionary<string, Tensor<float>> inputs);
    }
}
