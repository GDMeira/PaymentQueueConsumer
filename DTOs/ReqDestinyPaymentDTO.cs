using Consumer.Models;

namespace Consumer.DTOs;
public class ReqDestinyPaymentDTO(Payment payment)
{
    public OriginDTO Origin { get; set; } = new OriginDTO(payment.PaymentProviderAccount);

    public DestinyDTO Destiny { get; set; } = new DestinyDTO(payment.PixKey);
    public int Id { get; set; } = payment.Id;
    public DateTime CreatedAt { get; set; } = payment.CreatedAt;

    public int Amount { get; set; } = payment.Amount;

    public string? Description { get; set; } = payment.Description;

    public string Status { get; set; } = payment.Status;

    public class OriginDTO(PaymentProviderAccount account)
    {
        public AccountDTO Account { get; set; } = new AccountDTO(account.Number, account.Agency);
        public UserDTO User { get; set; } = new UserDTO(account.User.Cpf);
    }

    public class DestinyDTO(PixKey key)
    {
        public KeyDTO Key { get; set; } = new KeyDTO(key.Type, key.Value);
    }

    public class UserDTO(string cpf)
    {
        public string Cpf { get; set; } = cpf;
    }

    public class AccountDTO(string number, string agency)
    {
        public string Number { get; set; } = number;
        public string Agency { get; set; } = agency;
    }

    public class KeyDTO(string type, string value)
    {
        public string Type { get; set; } = type;
        public string Value { get; set; } = value;
    }
}