namespace AgentRegistry.Domain.Agents;

public readonly record struct CapabilityId(Guid Value)
{
    public static CapabilityId New() => new(Guid.NewGuid());
    public static CapabilityId Parse(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}
