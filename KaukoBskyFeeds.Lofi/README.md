# Lofi Bsky

A simple CLI stream for watching people you follow via the Jetstream.

## Config
A minimal config at `bsky.config.json` is required:

```
{
    "BskyConfig": {
        "Auth": {
            "Username": "YOUR_BSKY_HANDLE",
            "Password": "YOUR_APP_PASSWORD"
        }
    }
}
```

## Publishing

For example, to publish for Linux:

```
dotnet publish --os linux --arch x64 -p:PublishSingleFile=true --self-contained KaukoBskyFeeds.Lofi
```

Output is at `./KaukoBskyFeeds.Lofi/bin/Release/net8.0/linux-x64/publish/KaukoBskyFeeds.Lofi`
