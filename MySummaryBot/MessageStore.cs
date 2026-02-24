using System.Collections.Concurrent;

public class MessageStore
{
    readonly ConcurrentDictionary<long, List<MessageModel>> _messages = new();
    readonly ConcurrentDictionary<long, object> _locks = new();

    public void EnsureChat(long chatId)
    {
        _messages.GetOrAdd(chatId, _ => new List<MessageModel>());
        _locks.GetOrAdd(chatId, _ => new object());
    }

    public void AddMessage(MessageModel message)
    {
        var lockObj = _locks.GetOrAdd(message.ChatId, _ => new object());
        lock (lockObj)
            _messages.GetOrAdd(message.ChatId, _ => new List<MessageModel>()).Add(message);
    }

    public List<MessageModel> GetMessages(long chatId, TimeSpan age)
    {
        var cutoff = DateTime.Now - age;
        var lockObj = _locks.GetOrAdd(chatId, _ => new object());
        lock (lockObj)
            return _messages.GetOrAdd(chatId, _ => new List<MessageModel>())
                .Where(m => m.Timestamp > cutoff).ToList();
    }

    public MessageModel? FindMessage(long chatId, int messageId)
    {
        var lockObj = _locks.GetOrAdd(chatId, _ => new object());
        lock (lockObj)
            return _messages.GetOrAdd(chatId, _ => new List<MessageModel>())
                .FirstOrDefault(m => m.MessageId == messageId);
    }

    public void ClearOld()
    {
        var cutoff = DateTime.Now.AddDays(-1);
        foreach (var chatId in _messages.Keys)
        {
            var lockObj = _locks.GetOrAdd(chatId, _ => new object());
            lock (lockObj)
                _messages[chatId] = _messages[chatId].Where(m => m.Timestamp > cutoff).ToList();
        }
    }
}
