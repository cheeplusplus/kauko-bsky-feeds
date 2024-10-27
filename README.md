# Kauko's bsky feeds

This is mostly my own deployment of feeds to my personal bsky account, [kauko.biosynth.link](https://bsky.app/profile/kauko.biosynth.link).

I wanted to open source it since I couldn't find any feed generators written in C#.

## Notes

This requires a file called `bsky.config.json` that looks like this:
```
{
    "BskyConfig": {
        "Auth": {
            "Username": "YOUR_BSKY_HANDLE",
            "Password": "YOUR_APP_PASSWORD"
        },
        "Identity": {
            "Hostname": "SERVER_HOSTNAME",
            "PublishedAtUri": "at://YOUR_BSKY_DID"
        },
        "FeedProcessors": {
            "FEED_NAME": {
                "Type": "TimelineMinusList",
                "Config": {
                    "DisplayName": "SAMPLE DISPLAY NAME",
                    "Description": "SAMPLE DESCRIPTION",
                    "ListUri": "at://YOUR_BSKY_DID/app.bsky.graph.list/TARGET_LIST_ID"
                }
            }
        }
    }
}
```

To publish it as a Docker container, run
```
dotnet publish --os linux --arch x64 /t:PublishContainer
```
