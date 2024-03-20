namespace Consumer.Exceptions;

public class PaymentExpiredException(string message) : Exception(message)
{
}