using System;
using Microsoft.InformationProtection;

class ConsentDelegateImpl : IConsentDelegate
{
    public Consent GetUserConsent(string url)
    {
        return Consent.AcceptAlways;
    }
}