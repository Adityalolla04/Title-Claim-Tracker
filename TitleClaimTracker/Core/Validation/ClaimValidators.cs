// File: TitleClaimTracker/Core/Validation/ClaimValidators.cs
using System.Linq.Expressions;
using FluentValidation;
using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Core.Validation;

public sealed class CreateClaimValidator : AbstractValidator<CreateClaimDto>
{
    public CreateClaimValidator()
    {
        ClaimRules(this, x => x.Address, x => x.City, x => x.State, x => x.FilingType);
        RuleFor(x => x.DateFiled).NotEmpty();
        RuleFor(x => x.RiskScore).InclusiveBetween(0, 1).When(x => x.RiskScore.HasValue);
    }

    internal static void ClaimRules<T>(AbstractValidator<T> validator, Expression<Func<T, string>> address, Expression<Func<T, string>> city, Expression<Func<T, string>> state, Expression<Func<T, string>> filingType)
    {
        validator.RuleFor<string>(address).NotEmpty().MaximumLength(255);
        validator.RuleFor<string>(city).NotEmpty().MaximumLength(100);
        validator.RuleFor<string>(state).Length(2).Matches("^[A-Za-z]{2}$");
        validator.RuleFor<string>(filingType).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateClaimValidator : AbstractValidator<UpdateClaimDto>
{
    public UpdateClaimValidator()
    {
        CreateClaimValidator.ClaimRules(this, x => x.Address, x => x.City, x => x.State, x => x.FilingType);
        RuleFor(x => x.DateFiled).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty().Must(BeBase64).WithMessage("The row version must be Base64 encoded.");
    }

    private static bool BeBase64(string value)
    {
        try
        {
            _ = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class UpdateClaimStatusValidator : AbstractValidator<UpdateClaimStatusDto>
{
    public UpdateClaimStatusValidator()
    {
        RuleFor(x => x.Status).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class SubmitTriageDraftValidator : AbstractValidator<SubmitTriageDraftDto>
{
    public SubmitTriageDraftValidator()
    {
        RuleFor(x => x.TriageAttemptID).GreaterThan(0);
        CreateClaimValidator.ClaimRules(this, x => x.Address, x => x.City, x => x.State, x => x.FilingType);
    }
}

public sealed class ChatTriageValidator : AbstractValidator<ChatTriageRequestDto>
{
    public ChatTriageValidator() => RuleFor(x => x.Message).NotEmpty().MinimumLength(10).MaximumLength(10_000);
}
