using Consumer.Models;

namespace Consumer.DTOs;
public class ReqOriginPaymentDTO(Payment payment)
{
  public long Id { get; set; } = payment.Id;
  public string Status { get; set; } = payment.Status;
}