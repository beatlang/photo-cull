using System;
using Xunit;
using PCull.Ai.Inference;

namespace PCull.Tests.Inference
{
    public class MissingModelFileTests
    {
        [Fact]
        public void Constructor_Throws_When_Model_Missing()
        {
            var path = "nonexistent-model.onnx";
            Assert.Throws<System.IO.FileNotFoundException>(() => new OnnxInferenceEngine(path));
        }
    }
}
