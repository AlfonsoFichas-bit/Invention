using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace Invention;

public sealed class ComicLibraryRepository
{
    private readonly string _connectionString;

    public ComicLibraryRepository()
    {
        var databaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Invention");
        Directory.CreateDirectory(databaseDirectory);

        var databasePath = Path.Combine(databaseDirectory, "comics.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Comics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Title TEXT NOT NULL,
                AccessToken TEXT NOT NULL,
                AddedUtc INTEGER NOT NULL,
                LastOpenedUtc INTEGER NULL,
                LastReadPage INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();

        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(Comics);";
        using var columnsReader = columnsCommand.ExecuteReader();
        var hasLastReadPage = false;
        while (columnsReader.Read())
        {
            if (columnsReader.GetString(1) == "LastReadPage")
            {
                hasLastReadPage = true;
                break;
            }
        }

        columnsReader.Close();
        if (!hasLastReadPage)
        {
            using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = "ALTER TABLE Comics ADD COLUMN LastReadPage INTEGER NOT NULL DEFAULT 0;";
            migrationCommand.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ComicLibraryItem> GetAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FilePath, Title, AccessToken, AddedUtc, LastOpenedUtc, LastReadPage
            FROM Comics
            ORDER BY COALESCE(LastOpenedUtc, AddedUtc) DESC, Title COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var comics = new List<ComicLibraryItem>();
        while (reader.Read())
        {
            comics.Add(new ComicLibraryItem
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                Title = reader.GetString(2),
                AccessToken = reader.GetString(3),
                AddedUtc = reader.GetInt64(4),
                LastOpenedUtc = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                LastReadPage = reader.GetInt32(6)
            });
        }

        return comics;
    }

    public ComicLibraryItem? GetByPath(string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FilePath, Title, AccessToken, AddedUtc, LastOpenedUtc, LastReadPage
            FROM Comics
            WHERE FilePath = $path;
            """;
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ComicLibraryItem
        {
            Id = reader.GetInt64(0),
            Path = reader.GetString(1),
            Title = reader.GetString(2),
            AccessToken = reader.GetString(3),
            AddedUtc = reader.GetInt64(4),
            LastOpenedUtc = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            LastReadPage = reader.GetInt32(6)
        };
    }

    public void AddOrUpdate(string path, string title, string accessToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Comics (FilePath, Title, AccessToken, AddedUtc)
            VALUES ($path, $title, $accessToken, $addedUtc)
            ON CONFLICT(FilePath) DO UPDATE SET
                Title = excluded.Title,
                AccessToken = excluded.AccessToken;
            """;
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$accessToken", accessToken);
        command.Parameters.AddWithValue("$addedUtc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public void RemoveById(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Comics WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void MarkOpened(string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Comics
            SET LastOpenedUtc = $openedUtc
            WHERE FilePath = $path;
            """;
        command.Parameters.AddWithValue("$openedUtc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.ExecuteNonQuery();
    }

    public void UpdateLastReadPage(string path, int pageIndex)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Comics
            SET LastReadPage = $pageIndex
            WHERE FilePath = $path;
            """;
        command.Parameters.AddWithValue("$pageIndex", Math.Max(0, pageIndex));
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.ExecuteNonQuery();
    }
}
