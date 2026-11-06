using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using WpfLearning.Models;

namespace WpfLearning.Services;

/// <summary>
/// 収集記録の保存先。データベースファイル自体を SQLCipher で暗号化し、
/// その鍵は DPAPI の <see cref="DataProtectionScope.CurrentUser"/> で保護する。
/// 保存場所はログイン中のユーザーの LocalApplicationData\WpfLearning で、exe の位置には依存しない。
/// </summary>
public sealed class SqliteWasteStore
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>現在の鍵ファイルを DPAPI で包むときの追加データ。データベースのパスワードそのものではない。</summary>
    private static readonly byte[] KeyEntropy = "WpfLearning.SqliteKey.v1"u8.ToArray();

    /// <summary>旧形式の .dat を DPAPI で開くときの追加データ。</summary>
    private static readonly byte[] LegacyEntropy = "WpfLearning.WasteRecords.v1"u8.ToArray();

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _keyPath;
    private readonly string _legacyDatPath;
    private readonly string _legacyJsonPath;
    private readonly string _connectionString;

    /// <summary>
    /// 保存先を用意し、データベースを開く。空なら旧ファイルを取り込み、それも無ければサンプルを 3 件入れる。
    /// </summary>
    public SqliteWasteStore()
    {
        Batteries_V2.Init();

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfLearning");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "waste-records.db");
        _keyPath = Path.Combine(directory, "waste-records.key");
        _legacyDatPath = Path.Combine(directory, "waste-records.dat");
        _legacyJsonPath = Path.Combine(directory, "waste-records.json");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToBase64String(LoadOrCreateKey())
        };
        _connectionString = builder.ToString();
        Execute(InitializeDatabase);
    }

    /// <summary>暗号化されたデータベースファイルの絶対パス。</summary>
    public string FilePath { get; }

    /// <summary>この起動で、空のデータベースへ学習用サンプルを入れたとき true。</summary>
    public bool CreatedSampleData { get; private set; }

    /// <summary>
    /// 記録を収集日の新しい順に読む。画像の本体は含めず、枚数だけ <see cref="WasteRecord.ImageCount"/> に入れる。
    /// </summary>
    public List<WasteRecord> Load()
    {
        return Execute(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT Id, CollectedOn, ManifestNumber, GeneratorName, WasteName,
                       Quantity, Unit, DisposalMethod, Notes, RegisteredAt,
                       (SELECT COUNT(*) FROM WasteRecordImage WHERE RecordId = WasteRecord.Id)
                FROM WasteRecord
                ORDER BY CollectedOn DESC, RegisteredAt DESC
                """;

            var records = new List<WasteRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(ReadRecord(reader));

            return records;
        });
    }

    /// <summary>記録と <see cref="WasteRecord.Images"/> を、1 つのトランザクションで新しい行として追加する。</summary>
    /// <param name="record">追加する記録。画像は <see cref="WasteRecord.Images"/> に入れる。</param>
    public void Add(WasteRecord record)
    {
        Execute(connection =>
        {
            using var transaction = connection.BeginTransaction();
            Insert(connection, record, transaction);
            InsertImages(connection, record, transaction);
            transaction.Commit();
        });
    }

    /// <summary>
    /// 既存の行を更新する。登録日時は変えない。
    /// 画像はいったん削除し、<see cref="WasteRecord.Images"/> の順で入れ直す。
    /// 対象の行が無いときは <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <param name="record">更新する記録。Id で対象行を決め、Images で画像を入れ直す。</param>
    public void Update(WasteRecord record)
    {
        Execute(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE WasteRecord
                SET CollectedOn = $collectedOn,
                    ManifestNumber = $manifestNumber,
                    GeneratorName = $generatorName,
                    WasteName = $wasteName,
                    Quantity = $quantity,
                    Unit = $unit,
                    DisposalMethod = $disposalMethod,
                    Notes = $notes
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$id", FormatId(record.Id));
            command.Parameters.AddWithValue("$collectedOn", record.CollectedOn.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$manifestNumber", record.ManifestNumber.Trim());
            command.Parameters.AddWithValue("$generatorName", record.GeneratorName.Trim());
            command.Parameters.AddWithValue("$wasteName", record.WasteName.Trim());
            command.Parameters.AddWithValue("$quantity", record.Quantity.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$unit", record.Unit);
            command.Parameters.AddWithValue("$disposalMethod", record.DisposalMethod);
            command.Parameters.AddWithValue("$notes", record.Notes.Trim());
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("この記録は見つかりませんでした。一覧を再読込してください。");

            DeleteImages(connection, record.Id, transaction);
            InsertImages(connection, record, transaction);
            transaction.Commit();
        });
    }

    /// <summary>記録と、その記録に付いた画像を同じトランザクションで削除する。</summary>
    /// <param name="id">削除する記録の Id。</param>
    public void Delete(Guid id)
    {
        Execute(connection =>
        {
            using var transaction = connection.BeginTransaction();
            DeleteImages(connection, id, transaction);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM WasteRecord WHERE Id = $id";
            command.Parameters.AddWithValue("$id", FormatId(id));
            command.ExecuteNonQuery();
            transaction.Commit();
        });
    }

    /// <summary>1 件の記録に付いた JPEG を、添付した順で読む。</summary>
    /// <param name="recordId">画像を読む記録の Id。</param>
    public List<RecordImage> LoadImages(Guid recordId)
    {
        return Execute(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT Id, Jpeg
                FROM WasteRecordImage
                WHERE RecordId = $recordId
                ORDER BY SortOrder, Id
                """;
            command.Parameters.AddWithValue("$recordId", FormatId(recordId));
            var images = new List<RecordImage>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                images.Add(new RecordImage
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Jpeg = reader.GetFieldValue<byte[]>(1)
                });
            }

            return images;
        });
    }

    /// <summary>
    /// 同じマニフェスト番号が既にあるか。空文字は重複とみなさない。大文字小文字は区別しない。
    /// </summary>
    /// <param name="manifestNumber">調べる番号。空白だけなら重複なしとして false。</param>
    /// <param name="exceptId">除外する記録の Id。編集中の行自身を重複とみなさないために渡す。</param>
    public bool ManifestNumberExists(string manifestNumber, Guid? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(manifestNumber))
            return false;

        return Execute(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 1
                FROM WasteRecord
                WHERE ManifestNumber = $manifest COLLATE NOCASE
                  AND ($except IS NULL OR Id <> $except)
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$manifest", manifestNumber.Trim());
            command.Parameters.AddWithValue("$except", exceptId is Guid id ? FormatId(id) : DBNull.Value);
            return command.ExecuteScalar() is not null;
        });
    }

    /// <summary>
    /// テーブルが無ければ作る。記録が 0 件なら古い json または dat を取り込み、それも無ければサンプルを入れる。
    /// </summary>
    /// <param name="connection">開いているデータベース接続。</param>
    private void InitializeDatabase(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA journal_mode = DELETE;
                CREATE TABLE IF NOT EXISTS WasteRecord (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CollectedOn TEXT NOT NULL,
                    ManifestNumber TEXT NOT NULL DEFAULT '',
                    GeneratorName TEXT NOT NULL,
                    WasteName TEXT NOT NULL,
                    Quantity TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    DisposalMethod TEXT NOT NULL,
                    Notes TEXT NOT NULL DEFAULT '',
                    RegisteredAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_WasteRecord_CollectedOn
                    ON WasteRecord (CollectedOn DESC, RegisteredAt DESC);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_WasteRecord_ManifestNumber
                    ON WasteRecord (ManifestNumber COLLATE NOCASE)
                    WHERE ManifestNumber <> '';
                CREATE TABLE IF NOT EXISTS WasteRecordImage (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RecordId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    Jpeg BLOB NOT NULL,
                    FOREIGN KEY (RecordId) REFERENCES WasteRecord (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_WasteRecordImage_RecordId
                    ON WasteRecordImage (RecordId, SortOrder);
                """;
            command.ExecuteNonQuery();
        }

        if (Count(connection) == 0)
        {
            if (!TryImportLegacy(connection))
            {
                InsertMany(connection, CreateSample());
                CreatedSampleData = true;
            }
        }
        else
        {
            DeleteLegacyFiles();
        }
    }

    /// <summary>旧形式の記録があれば SQLite へ移し、元ファイルを削除する。無ければ false。</summary>
    /// <param name="connection">取り込み先の接続。</param>
    private bool TryImportLegacy(SqliteConnection connection)
    {
        List<WasteRecord>? records = null;
        if (File.Exists(_legacyDatPath))
            records = ReadLegacyDat();
        else if (File.Exists(_legacyJsonPath))
            records = ReadLegacyJson();

        if (records is null)
            return false;

        InsertMany(connection, records);
        DeleteLegacyFiles();
        return true;
    }

    /// <summary>WasteRecord の行数。</summary>
    /// <param name="connection">数える対象の接続。</param>
    private static int Count(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM WasteRecord";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>複数の記録を 1 つのトランザクションで追加する。サンプル投入と旧データの移行で使う。</summary>
    /// <param name="connection">追加先の接続。</param>
    /// <param name="records">追加する記録。画像は含めない。</param>
    private static void InsertMany(SqliteConnection connection, IReadOnlyList<WasteRecord> records)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var record in records)
            Insert(connection, record, transaction);
        transaction.Commit();
    }

    /// <summary>記録 1 件を挿入する。画像は含めない。</summary>
    /// <param name="connection">挿入先の接続。</param>
    /// <param name="record">挿入する記録。</param>
    /// <param name="transaction">同じトランザクションに含めるとき渡す。省略すると自動では開始しない。</param>
    private static void Insert(SqliteConnection connection, WasteRecord record, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO WasteRecord (
                Id, CollectedOn, ManifestNumber, GeneratorName, WasteName,
                Quantity, Unit, DisposalMethod, Notes, RegisteredAt)
            VALUES (
                $id, $collectedOn, $manifestNumber, $generatorName, $wasteName,
                $quantity, $unit, $disposalMethod, $notes, $registeredAt)
            """;
        command.Parameters.AddWithValue("$id", FormatId(record.Id));
        command.Parameters.AddWithValue("$collectedOn", record.CollectedOn.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$manifestNumber", record.ManifestNumber.Trim());
        command.Parameters.AddWithValue("$generatorName", record.GeneratorName.Trim());
        command.Parameters.AddWithValue("$wasteName", record.WasteName.Trim());
        command.Parameters.AddWithValue("$quantity", record.Quantity.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$unit", record.Unit);
        command.Parameters.AddWithValue("$disposalMethod", record.DisposalMethod);
        command.Parameters.AddWithValue("$notes", record.Notes.Trim());
        command.Parameters.AddWithValue("$registeredAt", record.RegisteredAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <summary>一覧の 1 行を <see cref="WasteRecord"/> にする。画像本体は読まない。</summary>
    /// <param name="reader">Load の SELECT 順で現在行を指しているリーダー。</param>
    private static WasteRecord ReadRecord(SqliteDataReader reader)
    {
        var collectedOn = DateTime.ParseExact(
            reader.GetString(1),
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);

        return new WasteRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            CollectedOn = DateTime.SpecifyKind(collectedOn, DateTimeKind.Unspecified),
            ManifestNumber = reader.GetString(2),
            GeneratorName = reader.GetString(3),
            WasteName = reader.GetString(4),
            Quantity = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            Unit = reader.GetString(6),
            DisposalMethod = reader.GetString(7),
            Notes = reader.GetString(8),
            RegisteredAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ImageCount = Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture)
        };
    }

    /// <summary>添付画像を、リストの順を SortOrder として挿入する。</summary>
    /// <param name="connection">挿入先の接続。</param>
    /// <param name="record">画像の親になる記録。Images の並びが SortOrder になる。</param>
    /// <param name="transaction">記録本体と同じトランザクション。</param>
    private static void InsertImages(SqliteConnection connection, WasteRecord record, SqliteTransaction transaction)
    {
        for (var index = 0; index < record.Images.Count; index++)
        {
            var image = record.Images[index];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO WasteRecordImage (Id, RecordId, SortOrder, Jpeg)
                VALUES ($id, $recordId, $sortOrder, $jpeg)
                """;
            command.Parameters.AddWithValue("$id", FormatId(image.Id));
            command.Parameters.AddWithValue("$recordId", FormatId(record.Id));
            command.Parameters.AddWithValue("$sortOrder", index);
            var jpeg = command.Parameters.Add("$jpeg", SqliteType.Blob);
            jpeg.Value = image.Jpeg;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>指定した記録に付いている画像をすべて削除する。</summary>
    /// <param name="connection">削除先の接続。</param>
    /// <param name="recordId">画像を消す記録の Id。</param>
    /// <param name="transaction">記録の削除または更新と同じトランザクション。</param>
    private static void DeleteImages(SqliteConnection connection, Guid recordId, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM WasteRecordImage WHERE RecordId = $recordId";
        command.Parameters.AddWithValue("$recordId", FormatId(recordId));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 鍵ファイルがあれば DPAPI で復号する。無ければ 32 バイトを作り、同じユーザーでだけ復号できる形で保存する。
    /// </summary>
    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
            return ProtectedData.Unprotect(File.ReadAllBytes(_keyPath), KeyEntropy, DataProtectionScope.CurrentUser);

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(key, KeyEntropy, DataProtectionScope.CurrentUser);
        var temporary = _keyPath + ".tmp";
        File.WriteAllBytes(temporary, protectedKey);
        File.Move(temporary, _keyPath, overwrite: true);
        return key;
    }

    /// <summary>DPAPI で暗号化されていた旧 .dat を、記録の一覧として読む。</summary>
    private List<WasteRecord> ReadLegacyDat()
    {
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_legacyDatPath), LegacyEntropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<WasteRecord>>(plain, LegacyJsonOptions) ?? [];
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException(
                $"これまでの記録ファイルを解読できませんでした。{Environment.NewLine}{_legacyDatPath}",
                exception);
        }
    }

    /// <summary>暗号化されていなかった旧 JSON を、記録の一覧として読む。</summary>
    private List<WasteRecord> ReadLegacyJson()
    {
        try
        {
            return JsonSerializer.Deserialize<List<WasteRecord>>(File.ReadAllBytes(_legacyJsonPath), LegacyJsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"これまでの記録ファイルを解釈できませんでした。{Environment.NewLine}{_legacyJsonPath}",
                exception);
        }
    }

    /// <summary>取り込み済みの旧 json と dat を削除する。</summary>
    private void DeleteLegacyFiles()
    {
        if (File.Exists(_legacyDatPath))
            File.Delete(_legacyDatPath);
        if (File.Exists(_legacyJsonPath))
            File.Delete(_legacyJsonPath);
    }

    /// <summary>戻り値のないデータベース操作を実行する。</summary>
    /// <param name="action">開いた接続に対して行う処理。</param>
    private void Execute(Action<SqliteConnection> action)
    {
        Execute(connection =>
        {
            action(connection);
            return 0;
        });
    }

    /// <summary>
    /// 接続を開き、外部キーを有効にしてから処理する。
    /// 失敗は保存先パスを付けた <see cref="InvalidOperationException"/> にする。
    /// </summary>
    /// <param name="action">開いた接続に対して行う処理。</param>
    private T Execute<T>(Func<SqliteConnection, T> action)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }

            return action(connection);
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException(
                $"記録データベースの操作に失敗しました。{Environment.NewLine}{FilePath}",
                exception);
        }
    }

    /// <summary>Guid を SQLite の TEXT 主キーに使う 36 文字の形式にする。</summary>
    /// <param name="id">保存する識別子。</param>
    private static string FormatId(Guid id) => id.ToString("D");

    /// <summary>初回起動用の学習サンプルを 3 件作る。画像は付けない。</summary>
    private static List<WasteRecord> CreateSample()
    {
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        return
        [
            new WasteRecord
            {
                Id = Guid.NewGuid(),
                CollectedOn = today.AddDays(-1),
                ManifestNumber = "SAMPLE-0001",
                GeneratorName = "株式会社あおば製作所",
                WasteName = "廃プラスチック類",
                Quantity = 120,
                Unit = "kg",
                DisposalMethod = "再生利用",
                Notes = "成形端材。学習用のサンプルです。",
                RegisteredAt = DateTime.Now.AddDays(-1)
            },
            new WasteRecord
            {
                Id = Guid.NewGuid(),
                CollectedOn = today.AddDays(-3),
                ManifestNumber = "SAMPLE-0002",
                GeneratorName = "北町建設株式会社",
                WasteName = "がれき類",
                Quantity = 2.4m,
                Unit = "t",
                DisposalMethod = "埋立",
                Notes = "コンクリートがら。学習用のサンプルです。",
                RegisteredAt = DateTime.Now.AddDays(-3)
            },
            new WasteRecord
            {
                Id = Guid.NewGuid(),
                CollectedOn = today.AddDays(-6),
                ManifestNumber = "SAMPLE-0003",
                GeneratorName = "みどり化学工業株式会社",
                WasteName = "廃油",
                Quantity = 80,
                Unit = "kg",
                DisposalMethod = "焼却",
                Notes = "作動油。学習用のサンプルです。",
                RegisteredAt = DateTime.Now.AddDays(-6)
            }
        ];
    }
}
