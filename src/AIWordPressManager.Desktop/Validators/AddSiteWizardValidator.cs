using FluentValidation;

namespace AIWordPressManager.Desktop.Validators;

public sealed class AddSiteWizardValidator : AbstractValidator<AddSiteWizardInput>
{
    public AddSiteWizardValidator()
    {
        RuleFor(x => x.SiteName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SiteUrl).NotEmpty().Must(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage("Enter a valid HTTP or HTTPS URL.");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ApplicationPassword).NotEmpty().MinimumLength(8)
            .WithMessage("Enter the WordPress Application Password.");
    }
}
