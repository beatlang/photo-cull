using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoCull.Core.Abstractions;

namespace PhotoCull.Ai.Inference
{
    public class OnnxInferenceEngine : IInferenceEngine
    {
        private readonly string _modelPath;
        private readonly SessionOptions _options;
        private InferenceSession? _session;

                public InferenceProvider ActiveProvider { get; private set; }

        public OnnxInferenceEngine(string modelPath, bool forceDmlFailure = false)
        {
            if (string.IsNullOrEmpty(modelPath)) throw new ArgumentException("modelPath");
            if (!File.Exists(modelPath)) throw new FileNotFoundException("Model file not found", modelPath);
            _modelPath = modelPath;
            _options = new SessionOptions();
            try
            {
                if (forceDmlFailure) throw new Exception("Simulated DML failure");
                _options.AppendExecutionProvider_DML(0);
                ActiveProvider = InferenceProvider.DirectML;
            }
            catch
            {
                ActiveProvider = InferenceProvider.CPU;
            }
        }

                private void EnsureSession()
        {
            if (_session != null) return;
            _session = new InferenceSession(_modelPath, _options);
        }

        public IReadOnlyDictionary<string, float[]> Run(IReadOnlyDictionary<string, Tensor<float>> inputs)
        {
            EnsureSession();
            var named = inputs.Select(kv => NamedOnnxValue.CreateFromTensor(kv.Key, kv.Value)).ToList();
            using var results = _session!.Run(named);
            var dict = new Dictionary<string, float[]>();
            foreach (var r in results)
            {
                if (r.AsTensor<float>() is Tensor<float> t)
                {
                    dict[r.Name] = t.ToArray();
                }
            }
            return dict;
        }

                public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
