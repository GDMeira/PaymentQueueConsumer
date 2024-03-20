using System.Net.Http.Json;
using System.Text;
using Consumer.Data;
using Consumer.DTOs;
using Consumer.Exceptions;
using Consumer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

var dbOptions = new DbContextOptionsBuilder<ApixDbContext>()
    .UseNpgsql("Host=localhost;Port=5433;Database=APIx_DB;UserName=postgres;Password=postgres;") //TODO: mudar para endereço do container
    .Options;

var cacheOpts = new ConfigurationOptions
{
    EndPoints = { "localhost:6379" },
    AbortOnConnectFail = false
};
IDistributedCache cache = new RedisCache(new RedisCacheOptions
{
    Configuration = "localhost:6379"
});

var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5039") //TODO: mudar para endereço do container
};

var factory = new ConnectionFactory
{
    HostName = "localhost",
    Port = 5672,
    UserName = "guest",
    Password = "guest",
    VirtualHost = "/"
};
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

channel.QueueDeclare(queue: "payments",
                     durable: true,
                     exclusive: false,
                     autoDelete: false,
                     arguments: null);

var consumer = new EventingBasicConsumer(channel);
consumer.Received += (model, ea) => handleReceivedMessage(model, ea);


channel.BasicConsume(queue: "payments",
 autoAck: false,
 consumer: consumer
 );

Console.Read();

void handleReceivedMessage(object? model, BasicDeliverEventArgs ea)
{
    var body = ea.Body.ToArray();
    var message = Encoding.UTF8.GetString(body);
    var paymentId = int.Parse(message);
    using var db = new ApixDbContext(dbOptions);
    Payment? payment = null;

    try
    {
        payment = retrievePaymentById(paymentId, db);
        validatePayment(payment);
        HttpResponseMessage responseFromDestiny = sendRequestToDestiny(payment);
        validateDestinyResponse(responseFromDestiny, payment.CreatedAt);
        updatePaymentStatus(payment, "SUCCESS", db);
        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        sendRequestToOrigin(payment);
    }
    catch (PixDestinyFailException)
    {
        requeueOrReject(ea, body);

        if (payment != null)
        {
            updatePaymentStatus(payment, "FAILED", db);
            sendRequestToOrigin(payment);
        }

    }
    catch (Exception)
    {
        channel.BasicReject(deliveryTag: ea.DeliveryTag, requeue: false);

        if (payment == null)
        {
            return;
        }

        updatePaymentStatus(payment, "FAILED", db);
        sendRequestToOrigin(payment);
    }
};

Payment retrievePaymentById(int paymentId, ApixDbContext db)
{
    Payment? payment = null;
    string cacheKey = $"payment-{paymentId}";

    string? cachedPayment = cache.GetString(cacheKey);

    if (cachedPayment != null)
    {
        payment = JsonConvert.DeserializeObject<Payment>(cachedPayment);
    }
    else
    {
        payment = db.Payments
            .AsSplitQuery()
            .Include(p => p.PixKey)
            .Include(p => p.PixKey.PaymentProviderAccount)
            .Include(p => p.PixKey.PaymentProviderAccount.PaymentProvider)
            .FirstOrDefault(p => p.Id == paymentId);

        if (payment != null)
        {
            cache.SetString(cacheKey, JsonConvert.SerializeObject(payment));
        }
    }

    return payment ?? throw new PaymentNotFoundException("Payment not found");
}

void removeFromCache(int paymentId)
{
    string cacheKey = $"payment-{paymentId}";
    cache.Remove(cacheKey);
}

void updatePaymentStatus(Payment payment, string status, ApixDbContext db)
{
    payment.Status = status;
    payment.UpdatedAt = DateTime.UtcNow;
    db.Payments.Update(payment);
    db.SaveChanges();
    removeFromCache(payment.Id);
}

void validatePayment(Payment? payment)
{
    if (payment == null)
    {
        throw new PaymentNotFoundException("Payment not found");
    }

    DateTime now = DateTime.UtcNow;
    if (DateTime.Compare(now, payment.CreatedAt.AddMinutes(2)) > 0)
    {
        throw new PaymentExpiredException("Payment expired");
    }
}

HttpResponseMessage sendRequestToDestiny(Payment payment)
{
    string destinyUrl = payment.PixKey.PaymentProviderAccount.PaymentProvider.PostPaymentUrl;
    var paymentDTO = new ReqDestinyPaymentDTO(payment);

    var taskResponseFromDestiny = httpClient.PostAsJsonAsync(destinyUrl, paymentDTO);
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(0.7));

    var completedTask = Task.WhenAny(taskResponseFromDestiny, timeoutTask).Result;

    if (completedTask.Equals(timeoutTask))
    {
        throw new PixDestinyFailException("Payment expired");
    }

    return taskResponseFromDestiny.Result;
}

void validateDestinyResponse(HttpResponseMessage response, DateTime createdAt)
{
    DateTime now = DateTime.UtcNow;

    if (!response.IsSuccessStatusCode && DateTime.Compare(now, createdAt.AddMinutes(2)) < 0)
    {
        throw new PixDestinyFailException("Payment failed");
    }
    else if (!response.IsSuccessStatusCode)
    {
        throw new PaymentExpiredException("Payment expired");
    }
}

void sendRequestToOrigin(Payment payment)
{
    string originUrl = payment.PaymentProviderAccount.PaymentProvider.PatchPaymentUrl;
    httpClient.PatchAsJsonAsync(originUrl, new ReqOriginPaymentDTO(payment));
}

void requeueOrReject(BasicDeliverEventArgs ea, byte[] body)
{
    int retryCount = (int)ea.BasicProperties.Headers["retry-count"];

    if (retryCount < 3)
    {
        var newHeaders = ea.BasicProperties.Headers;
        newHeaders["retry-count"] = retryCount + 1;
        channel.BasicReject(deliveryTag: ea.DeliveryTag, requeue: false);
        channel.BasicPublish(
            exchange: "",
            routingKey: "payments",
            basicProperties: ea.BasicProperties,
            body: body
        );
    }
    else
    {
        channel.BasicReject(deliveryTag: ea.DeliveryTag, requeue: false);
    }
}