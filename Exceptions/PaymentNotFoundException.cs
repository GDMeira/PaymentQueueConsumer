namespace Consumer.Exceptions;

public class PaymentNotFoundException(string message) : Exception(message)
{
}