namespace AgentRegistry.Domain.Agents;

public enum TransportType
{
    Http,
    Amqp,           // RabbitMQ and other AMQP brokers
    AzureServiceBus,
}
