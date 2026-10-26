namespace ClaimFlow.Domain.Common;

public static class Money
{
    /// <summary>Rounds a currency amount to cents using the rounding convention payers use on remittances.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
