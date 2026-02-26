namespace MarimerLLC.AgentRegistry.Domain.ApiKeys;

public readonly record struct ApiKeyId(Guid Value)
{
    public static ApiKeyId New() => new(Guid.NewGuid());
    public static ApiKeyId Parse(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}
