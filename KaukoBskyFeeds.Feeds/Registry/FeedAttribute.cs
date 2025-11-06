namespace KaukoBskyFeeds.Feeds.Registry;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class BskyFeedAttribute : Attribute
{
    public BskyFeedAttribute(string name, Type configType, bool supportsInteraction = false)
    {
        this.Name = name;
        this.ConfigType = configType;
        this.SupportsInteraction = supportsInteraction;
    }

    public string Name { get; init; }
    public Type ConfigType { get; init; }
    public bool SupportsInteraction { get; init; }
}
