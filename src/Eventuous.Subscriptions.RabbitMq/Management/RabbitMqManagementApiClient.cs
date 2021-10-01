namespace Eventuous.Subscriptions.RabbitMq.Management {
    using System.Net.Http;
    using RabbitMQ.Client;

    public class RabbitMqManagementApiClient {
        private readonly HttpClient _httpClient;

        public RabbitMqManagementApiClient(ConnectionFactory connectionFactory) {
            _httpClient = new HttpClient();
        }
    }
}