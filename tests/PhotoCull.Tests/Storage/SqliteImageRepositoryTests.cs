using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoCull.Infrastructure.Storage;
using PhotoCull.Core.Abstractions;
using Xunit;

namespace PhotoCull.Tests.Storage
{
    public class SqliteImageRepositoryTests
    {
        [Fact]
        public async Task Upsert_SaveLoadEmbeddingAndScores_Work()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"photocull-test-{Guid.NewGuid()}.db");
            try
            {
                var conn = $"Data Source={dbPath}";
                                using var repo = new SqliteImageRepository(conn);

                var id = await repo.UpsertImageAsync("/tmp/img.jpg", 1234, 640, 480, "jpg", DateTime.UtcNow, DateTime.UtcNow);
                Assert.True(id > 0);

                var img = await repo.GetImageByPathAsync("/tmp/img.jpg");
                Assert.NotNull(img);
                Assert.Equal(640, img!.Width);

                var embedding = Enumerable.Range(0, 128).Select(i => (float)i / 128f).ToArray();
                await repo.SaveEmbeddingAsync(id, embedding);
                var loaded = await repo.LoadEmbeddingAsync(id);
                Assert.NotNull(loaded);
                Assert.Equal(embedding.Length, loaded!.Length);
                for (int i = 0; i < embedding.Length; i++) Assert.Equal(embedding[i], loaded[i]);

                var scores = new QualityScores(1.1f, 2.2f, 3.3f, 4.4f, 5.5f);
                await repo.SaveQualityScoresAsync(id, scores);
                var lScores = await repo.LoadQualityScoresAsync(id);
                Assert.NotNull(lScores);
                Assert.Equal(scores.Sharpness, lScores!.Sharpness);
                Assert.Equal(scores.Quality, lScores.Quality);

                await repo.SaveHashesAsync(id, 1234567890123, 2222, 3333);

                var setId = await repo.CreateSetAsync("test-set");
                Assert.True(setId > 0);
                await repo.SaveSetMembersAsync(setId, new[] { new SetMember(id, true, PhotoCull.Core.Recommendation.Keep) });

                await repo.ClearSetsAsync();
                Assert.Empty(await repo.ListSetsAsync());

                await repo.SaveUserDecisionAsync(id, "keep");
                var decision = await repo.LoadUserDecisionAsync(id);
                Assert.Equal("keep", decision);
            }
            finally
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
