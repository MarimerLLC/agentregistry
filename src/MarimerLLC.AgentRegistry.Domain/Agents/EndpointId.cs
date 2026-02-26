namespace MarimerLLC.AgentRegistry.Domain.Agents;

public readonly record struct EndpointId(Guid Value)
{
    public static EndpointId New() => new(Guid.NewGuid());
    public static EndpointId Parse(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}
