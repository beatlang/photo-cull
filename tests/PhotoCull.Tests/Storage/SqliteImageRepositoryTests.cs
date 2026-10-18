using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PhotoCull.Infrastructure.Storage;
using PhotoCull.Core.Abstractions;
using PhotoCull.Core.Culling;
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
                var otherId = await repo.UpsertImageAsync("/tmp/other.jpg", 567, 320, 240, "jpg", DateTime.UtcNow, DateTime.UtcNow);
                var match = new ClusterMatch(id, otherId, 1.25, 2, 3, 8, 2, 8, 0.95, 0.9,
                    ClusterCriterionEvaluation.CaptureTime | ClusterCriterionEvaluation.DHash);
                await repo.SaveSetMatchesAsync(setId, new[] { match });
                var loadedMatch = Assert.Single(await repo.LoadSetMatchesAsync(setId));
                Assert.Equal(match, loadedMatch);

                await repo.SaveHashesAsync(id, null, 999, null);
                await repo.SaveHashesAsync(id, 123, null, null);
                await repo.SaveHashesAsync(id, null, null, 456);
                using (var hashConnection = new SqliteConnection(conn))
                {
                    hashConnection.Open();
                    using var command = hashConnection.CreateCommand();
                    command.CommandText = "SELECT PHash, DHash, AHash FROM Hashes WHERE ImageId=@id";
                    command.Parameters.AddWithValue("@id", id);
                    using var reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(123, reader.GetInt64(0));
                    Assert.Equal(999, reader.GetInt64(1));
                    Assert.Equal(456, reader.GetInt64(2));
                }

                await repo.ClearSetsAsync();
                Assert.Empty(await repo.ListSetsAsync());
                Assert.Empty(await repo.LoadSetMatchesAsync(setId));

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
