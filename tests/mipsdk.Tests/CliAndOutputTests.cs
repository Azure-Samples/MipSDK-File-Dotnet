using Microsoft.InformationProtection.Exceptions;
using SdkAction = mipsdk.Action;

namespace mipsdk.Tests;

public sealed class CliAndOutputTests
{
    [Theory]
    [InlineData("--help", 0)]
    [InlineData("-h", 0)]
    [InlineData("--unknown", 2)]
    public async Task CliHasDeterministicCredentialFreeExitCodes(string argument, int expected)
    {
        Assert.Equal(expected, await Program.Main([argument]));
    }

    [Fact]
    public async Task ExtraArgumentsAreRejected() => Assert.Equal(2, await Program.Main(["--help", "extra"]));

    [Fact]
    public async Task PublicBlankConfigurationFailsWithoutSignIn()
    {
        Assert.Equal(2, await Program.RunAsync(["--check-config"],
            () => TestConfig.Create(("ida:ClientId", ""))));
    }

    [Fact]
    public async Task ValidConfigCheckNeverStartsAuthentication()
    {
        Assert.Equal(0, await Program.RunAsync(["--check-config"], () => TestConfig.Create()));
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(FormatException))]
    public async Task UnreadableOrMalformedConfigurationHasTheConfigurationExitCode(Type errorType)
    {
        var error = (Exception)Activator.CreateInstance(errorType, "synthetic-sensitive-config-error")!;
        var previous = Console.Error;
        using var errors = new StringWriter();
        try
        {
            Console.SetError(errors);
            Assert.Equal(2, await Program.RunAsync(["--check-config"], () => throw error));
            Assert.Contains("Configuration could not be read", errors.ToString());
            Assert.DoesNotContain("synthetic-sensitive-config-error", errors.ToString());
        }
        finally { Console.SetError(previous); }
    }

    [Fact]
    public async Task UnreviewedSdkArgumentMessagesAreNotPrinted()
    {
        var previous = Console.Error;
        using var errors = new StringWriter();
        try
        {
            Console.SetError(errors);
            Assert.Equal(2, await Program.RunAsync(["--check-config"],
                () => throw new ArgumentException("synthetic-sensitive-server-response")));
            Assert.DoesNotContain("synthetic-sensitive-server-response", errors.ToString());
            Assert.Contains("Invalid input or SDK argument", errors.ToString());
        }
        finally { Console.SetError(previous); }
    }

    [Fact]
    [Trait("Category", "Native")]
    public async Task NativeSmokeLoadsLibrariesWithoutConfigurationOrCredentials() =>
        Assert.Equal(0, await Program.Main(["--native-smoke"]));

    [Theory]
    [InlineData("missing")]
    [InlineData("same")]
    [InlineData("existing")]
    [InlineData("directory")]
    [InlineData("parent")]
    [InlineData("blank-input")]
    [InlineData("blank-output")]
    public void OutputValidationPreservesInputsAndExistingDestinations(string kind)
    {
        using var directory = new TestDirectory();
        var source = directory.File("input.docx");
        var destination = directory.File("output.docx");
        File.WriteAllText(source, "source");
        switch (kind)
        {
            case "missing": source = directory.File("missing.docx"); break;
            case "same": destination = source; break;
            case "existing": File.WriteAllText(destination, "existing"); break;
            case "directory": destination = directory.Path; break;
            case "parent": destination = directory.File(Path.Combine("missing", "output.docx")); break;
            case "blank-input": source = ""; break;
            case "blank-output": destination = ""; break;
        }
        Assert.Throws<InputValidationException>(() => SafeOutput.ValidatePaths(source, destination));
        Assert.Equal("source", File.ReadAllText(directory.File("input.docx")));
        if (kind == "existing") { Assert.Equal("existing", File.ReadAllText(destination)); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Display Name <synthetic-user@example.invalid>")]
    public void InvalidRecipientsAreRejected(string value) =>
        Assert.Throws<InputValidationException>(() => SdkAction.ValidateRecipients([value]));

    [Fact]
    public void RecipientListMustBeNonemptyAndIsDeduplicated()
    {
        Assert.Throws<InputValidationException>(() => SdkAction.ValidateRecipients([]));
        Assert.Single(SdkAction.ValidateRecipients(["synthetic-user@example.invalid", " synthetic-user@example.invalid "]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CancelledJustificationNeverRetries(string reason)
    {
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => Program.LabelInteractivelyAsync(_ =>
        {
            attempts++;
            throw new JustificationRequiredException("synthetic");
        }, default, new StringReader(reason), new StringWriter()));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task UserAuthorizedJustificationRetriesOnceWithoutMutatingOriginalOptions()
    {
        var attempts = 0;
        var original = new SdkAction.FileOptions { LabelId = FileScenario.LabelId };
        var result = await Program.LabelInteractivelyAsync(options =>
        {
            attempts++;
            if (options.Justification == null) { throw new JustificationRequiredException("synthetic"); }
            Assert.Equal("Approved synthetic reason", options.Justification);
            return Task.FromResult(new SdkAction.LabelingResult(false, null, FileScenario.LabelId, false));
        }, original, new StringReader("Approved synthetic reason\n"), new StringWriter());
        Assert.False(result.Changed);
        Assert.Equal(2, attempts);
        Assert.Null(original.Justification);
    }

    [Fact]
    public async Task RepeatedJustificationRequirementDoesNotLoop()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<JustificationRequiredException>(() => Program.LabelInteractivelyAsync(_ =>
        {
            attempts++;
            throw new JustificationRequiredException("synthetic");
        }, default, new StringReader("Approved synthetic reason\n"), new StringWriter()));
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CancelledAdhocPromptNeverRetries(string recipients)
    {
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => Program.LabelInteractivelyAsync(_ =>
        {
            attempts++;
            throw new AdhocProtectionRequiredException("synthetic");
        }, default, new StringReader(recipients), new StringWriter()));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExplicitAdhocRecipientsArePassedOnlyAfterUserInput()
    {
        var attempts = 0;
        await Program.LabelInteractivelyAsync(options =>
        {
            attempts++;
            if (options.AdhocRecipients == null) { throw new AdhocProtectionRequiredException("synthetic"); }
            Assert.Equal(["synthetic-user@example.invalid"], options.AdhocRecipients);
            return Task.FromResult(new SdkAction.LabelingResult(true, "synthetic.docx", FileScenario.LabelId, true));
        }, default, new StringReader("synthetic-user@example.invalid\n"), new StringWriter());
        Assert.Equal(2, attempts);
    }
}
