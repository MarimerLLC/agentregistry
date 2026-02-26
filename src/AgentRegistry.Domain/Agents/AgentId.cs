namespace MarimerLLC.AgentRegistry.Domain.Agents;

public readonly record struct AgentId(Guid Value)
{
    public static AgentId New() => new(Guid.NewGuid());
    public static AgentId Parse(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}
