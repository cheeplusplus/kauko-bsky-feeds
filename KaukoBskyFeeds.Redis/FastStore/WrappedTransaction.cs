using StackExchange.Redis;

namespace KaukoBskyFeeds.Redis.FastStore;

public static class WrappedTransactionExtensions
{
    public static async Task WithTransaction(
        this IDatabase db,
        Func<ITransaction, Task> callback,
        CommandFlags commandFlags = CommandFlags.None
    )
    {
        var tr = db.CreateTransaction();
        await callback(tr);
        await tr.ExecuteAsync(commandFlags);
    }
}
