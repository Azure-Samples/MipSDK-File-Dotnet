using Microsoft.InformationProtection;

internal sealed class ConsentDelegateImpl(Consent consent) : IConsentDelegate
{
    public Consent GetUserConsent(string url) => consent;
}
