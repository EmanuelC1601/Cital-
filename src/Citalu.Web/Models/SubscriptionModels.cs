using System.ComponentModel.DataAnnotations;
using Citalu.Domain;
using Citalu.Web.Services;

namespace Citalu.Web.Models;

public sealed record SubscriptionOwnerPage(string BusinessName, SubscriptionState State,
    BusinessSubscription Subscription, IReadOnlyList<SubscriptionPeriod> Periods,
    IReadOnlyList<SubscriptionPayment> Payments, IReadOnlyList<SubscriptionAudit> Audit,
    string PaymentInstructions, Guid RequestKey);

public sealed record PlatformSubscriptionRow(Guid BusinessId, string BusinessName, string Slug,
    string OwnerEmail, SubscriptionState State, BusinessSubscription Subscription,
    IReadOnlyList<SubscriptionPeriod> Periods, IReadOnlyList<SubscriptionPayment> Payments);

public sealed record PlatformSubscriptionAdminPage(IReadOnlyList<PlatformSubscriptionRow> Businesses,
    string PlanName, decimal MonthlyPrice, string Currency, string TaxDisplay, Guid RequestKey);

public sealed class ManualPaymentInput
{
    [Required] public Guid BusinessId { get; set; }
    [Range(typeof(decimal), "0.01", "99999999.99", ParseLimitsInInvariantCulture = true)] public decimal Amount { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "MXN";
    [Required, StringLength(120)] public string Reference { get; set; } = "";
    [Required] public DateTimeOffset PaidAtUtc { get; set; }
    [Required] public Guid RequestKey { get; set; }
}

public sealed class CorrectionInput
{
    [Required] public Guid PaymentId { get; set; }
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
    [Required] public Guid RequestKey { get; set; }
}
