using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace AICAD.Services
{
    /// <summary>
    /// Manages naming series and sequences using a small SQLite file.
    /// </summary>
    public class SeriesManager : IDisposable
    {
        private readonly string _dbPath;
        private SQLiteConnection _connection;

        public SeriesManager(string dbPath = null)
        {
            _dbPath = dbPath ?? SettingsManager.GetDatabasePath();
            InitializeDatabase();
        }

        public string GetDatabasePath() => _dbPath;

        private void InitializeDatabase()
        {
            try
            {
                var isNew = !File.Exists(_dbPath);
                _connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                _connection.Open();

                if (isNew)
                {
                    CreateTables();
                    SeedDefaultSeries();
                }

                AddinLogger.Log(nameof(SeriesManager), $"Database ready at {_dbPath}");
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), "InitializeDatabase failed", ex);
                throw;
            }
        }

        private void CreateTables()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS Series (
                    SeriesID TEXT PRIMARY KEY,
                    Description TEXT,
                    LastUsedSequence INTEGER DEFAULT 0,
                    SequenceFormat TEXT DEFAULT '0000',
                    CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                    ModifiedDate TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS PartHistory (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    SeriesID TEXT NOT NULL,
                    SequenceNumber INTEGER NOT NULL,
                    FullPartName TEXT NOT NULL,
                    FilePath TEXT,
                    CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (SeriesID) REFERENCES Series(SeriesID)
                );

                CREATE INDEX IF NOT EXISTS idx_series_id ON PartHistory(SeriesID);
                CREATE INDEX IF NOT EXISTS idx_created_date ON PartHistory(CreatedDate);
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void SeedDefaultSeries()
        {
            var defaults = new[]
            {
                "ASM|Assembly|0000",
                "FAB|Fabrication|0000",
                "MCH|Machined|0000",
                "SHT|SheetMetal|0000",
                "PUR|Purchased|0000",
                "HRD|Hardware|0000"
            };

            foreach (var entry in defaults)
            {
                var parts = entry.Split('|');
                AddSeries(parts[0], parts[1], parts[2]);
            }
        }

        public IReadOnlyList<string> GetAllSeries()
        {
            var list = new List<string>();
            try
            {
                const string sql = "SELECT SeriesID FROM Series ORDER BY SeriesID";
                using (var cmd = new SQLiteCommand(sql, _connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), "GetAllSeries failed", ex);
            }
            return list;
        }

        public int GetNextSequence(string seriesId)
        {
            try
            {
                const string sql = "SELECT LastUsedSequence FROM Series WHERE SeriesID = @SeriesID";
                using (var cmd = new SQLiteCommand(sql, _connection))
                {
                    cmd.Parameters.AddWithValue("@SeriesID", seriesId);
                    var result = cmd.ExecuteScalar();
                    return (result == null ? 0 : Convert.ToInt32(result)) + 1;
                }
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), $"GetNextSequence failed for {seriesId}", ex);
                return 1;
            }
        }

        public string GeneratePartName(string seriesId, int? sequenceNumber = null)
        {
            try
            {
                var seq = sequenceNumber ?? GetNextSequence(seriesId);
                var format = GetSequenceFormat(seriesId);
                var formatted = seq.ToString(new string('0', format.Length > 0 ? format.Length : 4));
                var name = $"{seriesId}-{formatted}";
                AddinLogger.Log(nameof(SeriesManager), $"Generated part name {name}");
                return name;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), "GeneratePartName failed", ex);
                return seriesId + "-0001";
            }
        }

        public bool CommitSequence(string seriesId, int sequenceNumber, string partName, string filePath = null)
        {
            try
            {
                using (var tx = _connection.BeginTransaction())
                {
                    const string updateSql = @"
                        UPDATE Series
                        SET LastUsedSequence = @Sequence,
                            ModifiedDate = CURRENT_TIMESTAMP
                        WHERE SeriesID = @SeriesID";
                    using (var cmd = new SQLiteCommand(updateSql, _connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@Sequence", sequenceNumber);
                        cmd.Parameters.AddWithValue("@SeriesID", seriesId);
                        cmd.ExecuteNonQuery();
                    }

                    const string insertSql = @"
                        INSERT INTO PartHistory (SeriesID, SequenceNumber, FullPartName, FilePath)
                        VALUES (@SeriesID, @Sequence, @PartName, @FilePath)";
                    using (var cmd = new SQLiteCommand(insertSql, _connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@SeriesID", seriesId);
                        cmd.Parameters.AddWithValue("@Sequence", sequenceNumber);
                        cmd.Parameters.AddWithValue("@PartName", partName);
                        cmd.Parameters.AddWithValue("@FilePath", filePath ?? string.Empty);
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                AddinLogger.Log(nameof(SeriesManager), $"Committed {seriesId}-{sequenceNumber}");
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), $"CommitSequence failed for {seriesId}-{sequenceNumber}", ex);
                return false;
            }
        }

        public bool AddSeries(string seriesId, string description = "", string sequenceFormat = "0000")
        {
            try
            {
                const string sql = @"
                    INSERT OR IGNORE INTO Series (SeriesID, Description, SequenceFormat, LastUsedSequence)
                    VALUES (@SeriesID, @Description, @Format, 0)";
                using (var cmd = new SQLiteCommand(sql, _connection))
                {
                    cmd.Parameters.AddWithValue("@SeriesID", seriesId);
                    cmd.Parameters.AddWithValue("@Description", description);
                    cmd.Parameters.AddWithValue("@Format", sequenceFormat);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SeriesManager), $"AddSeries failed for {seriesId}", ex);
                return false;
            }
        }

        private string GetSequenceFormat(string seriesId)
        {
            try
            {
                const string sql = "SELECT SequenceFormat FROM Series WHERE SeriesID = @SeriesID";
                using (var cmd = new SQLiteCommand(sql, _connection))
                {
                    cmd.Parameters.AddWithValue("@SeriesID", seriesId);
                    var result = cmd.ExecuteScalar();
                    return result?.ToString() ?? "0000";
                }
            }
            catch
            {
                return "0000";
            }
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                try
                {
                    _connection.Close();
                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    AddinLogger.Error(nameof(SeriesManager), "Dispose failed", ex);
                }
                _connection = null;
            }
        }
    }
}
