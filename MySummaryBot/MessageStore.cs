using Microsoft.Data.Sqlite;

public class MessageStore : IDisposable
{
    readonly SqliteConnection _connection;
    readonly object _lock = new();

    public MessageStore(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA busy_timeout=5000");
        Execute("""
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                msg_id INTEGER NOT NULL,
                chat_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                username TEXT,
                first_name TEXT,
                language TEXT,
                text TEXT,
                media_type TEXT,
                link_preview TEXT,
                url_normalized TEXT,
                fwd_channel_id INTEGER,
                fwd_message_id INTEGER,
                timestamp TEXT NOT NULL,
                reply_to INTEGER
            )
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_chat_ts ON messages(chat_id, timestamp)");
        Execute("CREATE INDEX IF NOT EXISTS idx_chat_user ON messages(chat_id, user_id)");
        TryExecute("ALTER TABLE messages ADD COLUMN media_unique_id TEXT");
        Execute("CREATE INDEX IF NOT EXISTS idx_url ON messages(chat_id, url_normalized) WHERE url_normalized IS NOT NULL");
        Execute("CREATE INDEX IF NOT EXISTS idx_fwd ON messages(chat_id, fwd_channel_id, fwd_message_id) WHERE fwd_channel_id IS NOT NULL");
        Execute("CREATE INDEX IF NOT EXISTS idx_media ON messages(chat_id, media_unique_id) WHERE media_unique_id IS NOT NULL");
        Execute("CREATE INDEX IF NOT EXISTS idx_ts ON messages(timestamp)");
        Execute("CREATE INDEX IF NOT EXISTS idx_username ON messages(chat_id, username)");
        Execute("""
            CREATE TABLE IF NOT EXISTS digest_log (
                chat_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                PRIMARY KEY (chat_id, date)
            )
            """);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    public void EnsureChat(long chatId) { }

    public void AddMessage(MessageModel msg)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO messages (msg_id, chat_id, user_id, username, first_name, language, text, media_type, link_preview, url_normalized, media_unique_id, fwd_channel_id, fwd_message_id, timestamp, reply_to)
                VALUES (@msg_id, @chat_id, @user_id, @username, @first_name, @language, @text, @media_type, @link_preview, @url_normalized, @media_unique_id, @fwd_channel_id, @fwd_message_id, @timestamp, @reply_to)
                """;
            cmd.Parameters.AddWithValue("@msg_id", msg.MessageId);
            cmd.Parameters.AddWithValue("@chat_id", msg.ChatId);
            cmd.Parameters.AddWithValue("@user_id", msg.UserId);
            cmd.Parameters.AddWithValue("@username", (object?)msg.Username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@first_name", (object?)msg.FirstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@language", (object?)msg.Language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@text", (object?)msg.Text ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@media_type", (object?)msg.MediaType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@link_preview", (object?)msg.LinkPreview ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@url_normalized", (object?)msg.UrlNormalized ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@media_unique_id", (object?)msg.MediaUniqueId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fwd_channel_id", (object?)msg.FwdChannelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fwd_message_id", (object?)msg.FwdMessageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@timestamp", msg.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"));
            cmd.Parameters.AddWithValue("@reply_to", (object?)msg.ReplyToMessageId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public List<MessageModel> GetMessages(long chatId, TimeSpan age)
    {
        var cutoff = DateTime.UtcNow.Subtract(age).ToString("yyyy-MM-ddTHH:mm:ss");
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM messages WHERE chat_id = @c AND timestamp > @cutoff ORDER BY timestamp ASC";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            return ReadMessages(cmd);
        }
    }

    public MessageModel? FindMessage(long chatId, int messageId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM messages WHERE chat_id = @c AND msg_id = @m LIMIT 1";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@m", messageId);
            return ReadMessages(cmd).FirstOrDefault();
        }
    }

    public void ClearOld()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM messages WHERE timestamp < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-ddTHH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateLinkPreview(long chatId, int msgId, string preview)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE messages SET link_preview = @p WHERE chat_id = @c AND msg_id = @m";
            cmd.Parameters.AddWithValue("@p", preview);
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@m", msgId);
            cmd.ExecuteNonQuery();
        }
    }

    public int CountUrlOccurrences(long chatId, string urlNormalized)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE chat_id = @c AND url_normalized = @u";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@u", urlNormalized);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public int CountFwdOccurrences(long chatId, long fwdChannelId, int fwdMessageId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE chat_id = @c AND fwd_channel_id = @fc AND fwd_message_id = @fm";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@fc", fwdChannelId);
            cmd.Parameters.AddWithValue("@fm", fwdMessageId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public int CountMediaOccurrences(long chatId, string mediaUniqueId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE chat_id = @c AND media_unique_id = @m";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@m", mediaUniqueId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public long? FindUserId(long chatId, string username)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT user_id FROM messages WHERE chat_id = @c AND LOWER(username) = LOWER(@u) ORDER BY timestamp DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@u", username);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToInt64(result);
        }
    }

    public List<MessageModel> GetUserMessages(long chatId, long userId, TimeSpan age)
    {
        var cutoff = DateTime.UtcNow.Subtract(age).ToString("yyyy-MM-ddTHH:mm:ss");
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM messages WHERE chat_id = @c AND user_id = @uid AND timestamp > @cutoff ORDER BY timestamp ASC";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            return ReadMessages(cmd);
        }
    }

    public List<(string? Username, string? FirstName, int Count)> GetStats(long chatId, TimeSpan age)
    {
        var cutoff = DateTime.UtcNow.Subtract(age).ToString("yyyy-MM-ddTHH:mm:ss");
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT m.username, m.first_name, q.cnt
                FROM (
                    SELECT user_id, COUNT(*) as cnt, MAX(timestamp) as last_ts
                    FROM messages
                    WHERE chat_id = @c AND timestamp > @cutoff
                    GROUP BY user_id
                ) q
                JOIN messages m ON m.chat_id = @c AND m.user_id = q.user_id AND m.timestamp = q.last_ts
                ORDER BY q.cnt DESC
                """;
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            var result = new List<(string?, string?, int)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt32(2)
                ));
            return result;
        }
    }

    public List<long> GetActiveChats(DateTime sinceDate)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT chat_id FROM messages WHERE timestamp >= @since";
            cmd.Parameters.AddWithValue("@since", sinceDate.ToString("yyyy-MM-ddTHH:mm:ss"));
            var result = new List<long>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetInt64(0));
            return result;
        }
    }

    public bool HasDigestBeenSent(long chatId, string date)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM digest_log WHERE chat_id = @c AND date = @d";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@d", date);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }

    public void MarkDigestSent(long chatId, string date)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO digest_log (chat_id, date) VALUES (@c, @d)";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@d", date);
            cmd.ExecuteNonQuery();
        }
    }

    List<MessageModel> ReadMessages(SqliteCommand cmd)
    {
        var result = new List<MessageModel>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MessageModel
            {
                MessageId = reader.GetInt32(reader.GetOrdinal("msg_id")),
                ChatId = reader.GetInt64(reader.GetOrdinal("chat_id")),
                UserId = reader.GetInt64(reader.GetOrdinal("user_id")),
                Username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString(reader.GetOrdinal("username")),
                FirstName = reader.IsDBNull(reader.GetOrdinal("first_name")) ? null : reader.GetString(reader.GetOrdinal("first_name")),
                Language = reader.IsDBNull(reader.GetOrdinal("language")) ? null : reader.GetString(reader.GetOrdinal("language")),
                Text = reader.IsDBNull(reader.GetOrdinal("text")) ? null : reader.GetString(reader.GetOrdinal("text")),
                MediaType = reader.IsDBNull(reader.GetOrdinal("media_type")) ? null : reader.GetString(reader.GetOrdinal("media_type")),
                LinkPreview = reader.IsDBNull(reader.GetOrdinal("link_preview")) ? null : reader.GetString(reader.GetOrdinal("link_preview")),
                UrlNormalized = reader.IsDBNull(reader.GetOrdinal("url_normalized")) ? null : reader.GetString(reader.GetOrdinal("url_normalized")),
                MediaUniqueId = reader.IsDBNull(reader.GetOrdinal("media_unique_id")) ? null : reader.GetString(reader.GetOrdinal("media_unique_id")),
                FwdChannelId = reader.IsDBNull(reader.GetOrdinal("fwd_channel_id")) ? null : reader.GetInt64(reader.GetOrdinal("fwd_channel_id")),
                FwdMessageId = reader.IsDBNull(reader.GetOrdinal("fwd_message_id")) ? null : reader.GetInt32(reader.GetOrdinal("fwd_message_id")),
                Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
                ReplyToMessageId = reader.IsDBNull(reader.GetOrdinal("reply_to")) ? null : reader.GetInt32(reader.GetOrdinal("reply_to"))
            });
        }
        return result;
    }

    void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    void TryExecute(string sql)
    {
        try { Execute(sql); } catch { }
    }
}
