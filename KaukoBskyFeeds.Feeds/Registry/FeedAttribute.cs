namespace KaukoBskyFeeds.Feeds.Registry;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class BskyFeedAttribute : Attribute
{
    public BskyFeedAttribute(string name, Type configType)
    {
        this.Name = name;
        this.ConfigType = configType;
    }

    public string Name { get; init; }
    public Type ConfigType { get; init; }
}
