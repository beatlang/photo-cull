using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PhotoCull.Core;
using PhotoCull.Core.Abstractions;

namespace PhotoCull.Infrastructure.Storage
{
    public class SqliteImageRepository : IImageRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection? _keepAliveConnection;

        public SqliteImageRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

            // If using in-memory shared cache, keep a connection open to persist data across connections
            if (_connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase) || _connectionString.Contains("DataSource=:memory:", StringComparison.OrdinalIgnoreCase) || _connectionString.Contains("Data Source=:memory:", StringComparison.OrdinalIgnoreCase))
            {
                _keepAliveConnection = new SqliteConnection(_connectionString);
                _keepAliveConnection.Open();
            }

            EnsureSchema();
        }

        private SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        private void EnsureSchema()
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Images (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL UNIQUE,
    SizeBytes INTEGER,
    Width INTEGER,
    Height INTEGER,
    Format TEXT,
    CreatedUtc TEXT,
    ModifiedUtc TEXT
);

CREATE TABLE IF NOT EXISTS ExifData (
    ImageId INTEGER PRIMARY KEY,
    Tag TEXT,
    Value TEXT,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Hashes (
    ImageId INTEGER PRIMARY KEY,
    PHash INTEGER,
    DHash INTEGER,
    AHash INTEGER,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Embeddings (
    ImageId INTEGER PRIMARY KEY,
    Data BLOB,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS QualityScores (
    ImageId INTEGER PRIMARY KEY,
    Sharpness REAL,
    Exposure REAL,
    Brightness REAL,
    Noise REAL,
    Quality REAL,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Sets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT UNIQUE
);

CREATE TABLE IF NOT EXISTS SetMembers (
    SetId INTEGER,
    ImageId INTEGER,
    IsBestShot INTEGER,
    Recommendation INTEGER,
    PRIMARY KEY(SetId, ImageId),
    FOREIGN KEY(SetId) REFERENCES Sets(Id) ON DELETE CASCADE,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS UserDecisions (
    ImageId INTEGER PRIMARY KEY,
    Action TEXT,
    FOREIGN KEY(ImageId) REFERENCES Images(Id) ON DELETE CASCADE
);
";
            cmd.ExecuteNonQuery();
        }

        public async Task<long> UpsertImageAsync(string path, long sizeBytes, int width, int height, string format, DateTime createdUtc, DateTime modifiedUtc)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();

            // Try update
            using (var update = conn.CreateCommand())
            {
                update.CommandText = @"UPDATE Images SET SizeBytes=@size, Width=@w, Height=@h, Format=@f, CreatedUtc=@c, ModifiedUtc=@m WHERE Path=@p";
                update.Parameters.AddWithValue("@size", sizeBytes);
                update.Parameters.AddWithValue("@w", width);
                update.Parameters.AddWithValue("@h", height);
                update.Parameters.AddWithValue("@f", format ?? string.Empty);
                update.Parameters.AddWithValue("@c", createdUtc.ToString("o"));
                update.Parameters.AddWithValue("@m", modifiedUtc.ToString("o"));
                update.Parameters.AddWithValue("@p", path);
                update.Transaction = tx;
                var rows = await update.ExecuteNonQueryAsync();
                if (rows > 0)
                {
                    using var sel = conn.CreateCommand();
                    sel.CommandText = "SELECT Id FROM Images WHERE Path=@p";
                    sel.Parameters.AddWithValue("@p", path);
                    sel.Transaction = tx;
                    var id = (long?)await sel.ExecuteScalarAsync();
                    tx.Commit();
                    return id ?? throw new Exception("Failed to read id after update");
                }
            }

            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = @"INSERT INTO Images (Path, SizeBytes, Width, Height, Format, CreatedUtc, ModifiedUtc) VALUES (@p,@size,@w,@h,@f,@c,@m); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("@p", path);
                insert.Parameters.AddWithValue("@size", sizeBytes);
                insert.Parameters.AddWithValue("@w", width);
                insert.Parameters.AddWithValue("@h", height);
                insert.Parameters.AddWithValue("@f", format ?? string.Empty);
                insert.Parameters.AddWithValue("@c", createdUtc.ToString("o"));
                insert.Parameters.AddWithValue("@m", modifiedUtc.ToString("o"));
                insert.Transaction = tx;
                var id = (long)await insert.ExecuteScalarAsync();
                tx.Commit();
                return id;
            }
        }

        public async Task<ImageRecord?> GetImageByPathAsync(string path)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Path, SizeBytes, Width, Height, Format, CreatedUtc, ModifiedUtc FROM Images WHERE Path=@p";
            cmd.Parameters.AddWithValue("@p", path);
            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return new ImageRecord(
                    rdr.GetInt64(0),
                    rdr.GetString(1),
                    rdr.IsDBNull(2) ? 0 : rdr.GetInt64(2),
                    rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3),
                    rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                    rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    DateTime.Parse(rdr.IsDBNull(6) ? DateTime.MinValue.ToString("o") : rdr.GetString(6)),
                    DateTime.Parse(rdr.IsDBNull(7) ? DateTime.MinValue.ToString("o") : rdr.GetString(7))
                );
            }
            return null;
        }

        public async Task<ImageRecord?> GetImageByIdAsync(long id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Path, SizeBytes, Width, Height, Format, CreatedUtc, ModifiedUtc FROM Images WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return new ImageRecord(
                    rdr.GetInt64(0),
                    rdr.GetString(1),
                    rdr.IsDBNull(2) ? 0 : rdr.GetInt64(2),
                    rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3),
                    rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                    rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    DateTime.Parse(rdr.IsDBNull(6) ? DateTime.MinValue.ToString("o") : rdr.GetString(6)),
                    DateTime.Parse(rdr.IsDBNull(7) ? DateTime.MinValue.ToString("o") : rdr.GetString(7))
                );
            }
            return null;
        }

        public async Task<IEnumerable<ImageRecord>> ListImagesAsync()
        {
            var list = new List<ImageRecord>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Path, SizeBytes, Width, Height, Format, CreatedUtc, ModifiedUtc FROM Images";
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new ImageRecord(
                    rdr.GetInt64(0),
                    rdr.GetString(1),
                    rdr.IsDBNull(2) ? 0 : rdr.GetInt64(2),
                    rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3),
                    rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                    rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    DateTime.Parse(rdr.IsDBNull(6) ? DateTime.MinValue.ToString("o") : rdr.GetString(6)),
                    DateTime.Parse(rdr.IsDBNull(7) ? DateTime.MinValue.ToString("o") : rdr.GetString(7))
                ));
            }
            return list;
        }

        public async Task SaveEmbeddingAsync(long imageId, float[] embedding)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO Embeddings (ImageId, Data) VALUES (@id,@data)";
            cmd.Parameters.AddWithValue("@id", imageId);
            var bytes = new byte[embedding.Length * 4];
            Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
            cmd.Parameters.AddWithValue("@data", bytes);
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public async Task<float[]?> LoadEmbeddingAsync(long imageId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Data FROM Embeddings WHERE ImageId=@id";
            cmd.Parameters.AddWithValue("@id", imageId);
            var obj = await cmd.ExecuteScalarAsync();
            if (obj == null || obj is DBNull) return null;
            var bytes = (byte[])obj;
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        public async Task SaveQualityScoresAsync(long imageId, QualityScores scores)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT OR REPLACE INTO QualityScores (ImageId, Sharpness, Exposure, Brightness, Noise, Quality) VALUES (@id,@s,@e,@b,@n,@q)";
            cmd.Parameters.AddWithValue("@id", imageId);
            cmd.Parameters.AddWithValue("@s", scores.Sharpness);
            cmd.Parameters.AddWithValue("@e", scores.Exposure);
            cmd.Parameters.AddWithValue("@b", scores.Brightness);
            cmd.Parameters.AddWithValue("@n", scores.Noise);
            cmd.Parameters.AddWithValue("@q", scores.Quality);
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public async Task<QualityScores?> LoadQualityScoresAsync(long imageId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Sharpness, Exposure, Brightness, Noise, Quality FROM QualityScores WHERE ImageId=@id";
            cmd.Parameters.AddWithValue("@id", imageId);
            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return new QualityScores(
                    rdr.IsDBNull(0) ? 0f : (float)rdr.GetDouble(0),
                    rdr.IsDBNull(1) ? 0f : (float)rdr.GetDouble(1),
                    rdr.IsDBNull(2) ? 0f : (float)rdr.GetDouble(2),
                    rdr.IsDBNull(3) ? 0f : (float)rdr.GetDouble(3),
                    rdr.IsDBNull(4) ? 0f : (float)rdr.GetDouble(4)
                );
            }
            return null;
        }

        public async Task SaveHashesAsync(long imageId, long? pHash, long? dHash, long? aHash)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT OR REPLACE INTO Hashes (ImageId, PHash, DHash, AHash) VALUES (@id,@p,@d,@a)";
            cmd.Parameters.AddWithValue("@id", imageId);
            cmd.Parameters.AddWithValue("@p", pHash.HasValue ? (object)pHash.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@d", dHash.HasValue ? (object)dHash.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@a", aHash.HasValue ? (object)aHash.Value : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public async Task<long> CreateSetAsync(string name)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO Sets (Name) VALUES (@name); SELECT Id FROM Sets WHERE Name=@name;";
            cmd.Parameters.AddWithValue("@name", name);
            var idObj = await cmd.ExecuteScalarAsync();
            tx.Commit();
            return Convert.ToInt64(idObj);
        }

        public async Task SaveSetMembersAsync(long setId, IEnumerable<SetMember> members)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            foreach (var m in members)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO SetMembers (SetId, ImageId, IsBestShot, Recommendation) VALUES (@set,@img,@best,@rec)";
                cmd.Parameters.AddWithValue("@set", setId);
                cmd.Parameters.AddWithValue("@img", m.ImageId);
                cmd.Parameters.AddWithValue("@best", m.IsBestShot ? 1 : 0);
                cmd.Parameters.AddWithValue("@rec", (int)m.Recommendation);
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }

        public async Task<IEnumerable<SetInfo>> ListSetsAsync()
        {
            var list = new List<SetInfo>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Sets ORDER BY Id";
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SetInfo(rdr.GetInt64(0), rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1)));
            }
            return list;
        }

        public async Task<IEnumerable<SetMember>> LoadSetMembersAsync(long setId)
        {
            var list = new List<SetMember>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ImageId, IsBestShot, Recommendation FROM SetMembers WHERE SetId=@set ORDER BY IsBestShot DESC";
            cmd.Parameters.AddWithValue("@set", setId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SetMember(
                    rdr.IsDBNull(0) ? 0 : rdr.GetInt64(0),
                    rdr.IsDBNull(1) ? false : rdr.GetInt32(1) != 0,
                    (Recommendation)(rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2))
                ));
            }
            return list;
        }

        public async Task SaveUserDecisionAsync(long imageId, string action)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO UserDecisions (ImageId, Action) VALUES (@id,@a)";
            cmd.Parameters.AddWithValue("@id", imageId);
            cmd.Parameters.AddWithValue("@a", action ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public async Task<string?> LoadUserDecisionAsync(long imageId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Action FROM UserDecisions WHERE ImageId=@id";
            cmd.Parameters.AddWithValue("@id", imageId);
            var obj = await cmd.ExecuteScalarAsync();
            if (obj == null || obj is DBNull) return null;
            return Convert.ToString(obj);
        }

        public void Dispose()
        {
            _keepAliveConnection?.Dispose();
        }
    }
}
