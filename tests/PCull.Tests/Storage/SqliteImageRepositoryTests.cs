using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PCull.Infrastructure.Storage;
using PCull.Core.Abstractions;
using PCull.Core.Culling;
using Xunit;

namespace PCull.Tests.Storage
{
    public class SqliteImageRepositoryTests
    {
        [Fact]
        public async Task Upsert_ChangedFile_InvalidatesCachedFeatures_UnchangedKeepsThem()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"pcull-test-{Guid.NewGuid()}.db");
            try
            {
                using var repo = new SqliteImageRepository($"Data Source={dbPath}");
                var modified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var id = await repo.UpsertImageAsync("/tmp/a.jpg", 100, 10, 10, "jpg", modified, modified);
                await repo.SaveHashesAsync(id, 1, 2, null);
                await repo.SaveQualityScoresAsync(id, new QualityScores(1, 2, 3, 4, 5));

                await repo.UpsertImageAsync("/tmp/a.jpg", 100, 10, 10, "jpg", modified, modified);
                var hashes = await repo.LoadHashesAsync(id);
                Assert.Equal(2, hashes!.DHash);
                Assert.NotNull(await repo.LoadQualityScoresAsync(id));

                await repo.UpsertImageAsync("/tmp/a.jpg", 101, 10, 10, "jpg", modified, modified);
                Assert.Null(await repo.LoadHashesAsync(id));
                Assert.Null(await repo.LoadQualityScoresAsync(id));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public async Task Upsert_SaveLoadEmbeddingAndScores_Work()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"pcull-test-{Guid.NewGuid()}.db");
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
                var otherId = await repo.UpsertImageAsync("/tmp/other.jpg", 567, 320, 240, "jpg", DateTime.UtcNow, DateTime.UtcNow);
                await repo.SaveSetMembersAsync(setId, new[]
                {
                    new SetMember(id, true, PCull.Core.Recommendation.Keep),
                    new SetMember(otherId, false, PCull.Core.Recommendation.Review)
                });
                var loadedMembers = (await repo.LoadSetMembersAsync(setId)).ToArray();
                Assert.Equal(2, loadedMembers.Length);
                Assert.Contains(loadedMembers, member => member.ImageId == id && member.IsBestShot);
                Assert.Contains(loadedMembers, member => member.ImageId == otherId && !member.IsBestShot);
                var match = new ClusterMatch(id, otherId, 1.25, 2, 3, 8, 2, 8, 0.95, 0.9,
                    ClusterCriterionEvaluation.CaptureTime | ClusterCriterionEvaluation.DHash);
                var reverseMatch = match with { ImageId = otherId, MatchedImageId = id };
                await repo.SaveSetMatchesAsync(setId, new[] { match, reverseMatch });
                var loadedMatches = (await repo.LoadSetMatchesAsync(setId)).ToArray();
                Assert.Equal(2, loadedMatches.Length);
                Assert.Contains(match, loadedMatches);
                Assert.Contains(reverseMatch, loadedMatches);

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

        [Fact]
        public async Task RemoveImagesNotInPaths_RemovesOldImagesAndTheirDependentData()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"pcull-test-{Guid.NewGuid()}.db");
            try
            {
                using var repo = new SqliteImageRepository($"Data Source={dbPath}");
                var timestamp = DateTime.UtcNow;
                var keptId = await repo.UpsertImageAsync("/photos/current.jpg", 100, 10, 10, "jpg", timestamp, timestamp);
                var oldId = await repo.UpsertImageAsync("/photos/old.jpg", 200, 10, 10, "jpg", timestamp, timestamp);
                await repo.SaveHashesAsync(keptId, 7, 8, 9);
                await repo.SaveUserDecisionAsync(keptId, "Keep");
                await repo.SaveHashesAsync(oldId, 1, 2, 3);
                await repo.SaveUserDecisionAsync(oldId, "Delete");

                await repo.RemoveImagesNotInPathsAsync(new[] { "/photos/current.jpg" });

                Assert.NotNull(await repo.GetImageByIdAsync(keptId));
                Assert.Null(await repo.GetImageByIdAsync(oldId));
                Assert.Equal(8, (await repo.LoadHashesAsync(keptId))!.DHash);
                Assert.Equal("Keep", await repo.LoadUserDecisionAsync(keptId));
                Assert.Null(await repo.LoadHashesAsync(oldId));
                Assert.Null(await repo.LoadUserDecisionAsync(oldId));
                Assert.Equal(new[] { "/photos/current.jpg" }, (await repo.ListImagesAsync()).Select(image => image.Path));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }
    }
}
