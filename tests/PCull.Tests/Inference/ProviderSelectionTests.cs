using System;
using System.IO;
using Xunit;
using PCull.Ai.Inference;
using PCull.Core.Abstractions;

namespace PCull.Tests.Inference
{
    public class ProviderSelectionTests
    {
        [Fact]
        public void ForceDmlFailure_Falls_Back_To_CPU()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "placeholder");
                using var engine = new OnnxInferenceEngine(tmp, forceDmlFailure: true);
                Assert.Equal(InferenceProvider.CPU, engine.ActiveProvider);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void Default_Attempt_Uses_DirectML_Or_CPU()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "placeholder");
                using var engine = new OnnxInferenceEngine(tmp, forceDmlFailure: false);
                Assert.True(engine.ActiveProvider == InferenceProvider.DirectML || engine.ActiveProvider == InferenceProvider.CPU);
            }
            finally { File.Delete(tmp); }
        }
    }
}
