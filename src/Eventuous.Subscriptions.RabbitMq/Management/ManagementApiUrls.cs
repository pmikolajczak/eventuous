namespace Eventuous.Subscriptions.RabbitMq.Management {
    internal static class ManagementApiUrls {
        
        // GET /api/queues/{vhost}/{qname}
        public const string QueueMetricsUrlFormat = "/api/queues/{0}/{1}";
    }
}