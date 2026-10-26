using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Common;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;

namespace ClaimFlow.Application.Pricing;

public sealed record PricingInput(int LineNumber, DateOnly ServiceDate, int Units, decimal BilledAmount, FeeScheduleEntry Fee);

public sealed record PricingResult(IReadOnlyDictionary<int, LinePricing> Lines, PlanAccumulators EndingAccumulators);

/// <summary>
/// Prices payable lines against the plan's benefit design.
/// <list type="bullet">
/// <item>Allowed = lesser of billed charge and fee schedule × units.</item>
/// <item>Office visits: flat copay, deductible not applied.</item>
/// <item>Everything else: deductible first, then coinsurance on the remainder.</item>
/// <item>Member cost share is capped at the remaining out-of-pocket maximum.</item>
/// </list>
/// Lines are priced in service-date order so accumulators build up the way they would in production.
/// </summary>
public sealed class ClaimPricingCalculator
{
    public PricingResult Price(HealthPlan plan, PlanAccumulators start, IEnumerable<PricingInput> lines)
    {
        var deductibleMet = start.DeductibleMet;
        var oopMet = start.OutOfPocketMet;
        var priced = new Dictionary<int, LinePricing>();

        foreach (var line in lines.OrderBy(l => l.ServiceDate).ThenBy(l => l.LineNumber))
        {
            var allowed = Money.Round(Math.Min(line.BilledAmount, line.Fee.AllowedAmount * line.Units));
            var oopRemaining = Math.Max(0m, plan.OutOfPocketMax - oopMet);

            decimal deductible = 0m, copay = 0m, coinsurance = 0m;

            if (line.Fee.IsOfficeVisit)
            {
                copay = Math.Min(Math.Min(plan.OfficeVisitCopay, allowed), oopRemaining);
            }
            else
            {
                var deductibleRemaining = Math.Max(0m, plan.AnnualDeductible - deductibleMet);
                deductible = Math.Min(deductibleRemaining, allowed);
                coinsurance = Money.Round((allowed - deductible) * plan.CoinsuranceRate);

                var excess = deductible + coinsurance - oopRemaining;
                if (excess > 0)
                {
                    var fromCoinsurance = Math.Min(coinsurance, excess);
                    coinsurance -= fromCoinsurance;
                    deductible -= excess - fromCoinsurance;
                }
            }

            var planPaid = allowed - deductible - copay - coinsurance;
            deductibleMet += deductible;
            oopMet += deductible + copay + coinsurance;

            priced[line.LineNumber] = new LinePricing(allowed, deductible, copay, coinsurance, planPaid);
        }

        return new PricingResult(priced, new PlanAccumulators(deductibleMet, oopMet));
    }
}
