using System.Collections.ObjectModel;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.InformationProtection.File;
using Microsoft.InformationProtection.Protection;
using Moq;
using SdkAction = mipsdk.Action;

namespace mipsdk.Tests;

public sealed class FileOperationTests
{
    [Fact]
    public async Task ChangedOutputIsVerifiedTwiceBeforeSuccessAudit()
    {
        using var scenario = new FileScenario();
        scenario.Options.GenerateChangeAuditEvent = true;
        scenario.Writer.Setup(handler => handler.NotifyCommitSuccessful(scenario.Options.OutputName))
            .Callback(() =>
            {
                Assert.Equal(3, scenario.ReadPaths.Count);
                Assert.True(File.Exists(scenario.Options.OutputName));
            });
        var result = await scenario.Action.SetLabelAsync(scenario.Options);
        Assert.True(result.Changed);
        Assert.Equal(scenario.Options.OutputName, result.OutputPath);
        Assert.False(result.IsProtected);
        Assert.Equal("synthetic source bytes", File.ReadAllText(scenario.Options.FileName));
        Assert.Equal("synthetic labeled bytes", File.ReadAllText(scenario.Options.OutputName));
        Assert.Equal(".docx", Path.GetExtension(scenario.ReadPaths[1]));
        Assert.Empty(Directory.GetFiles(scenario.Directory.Path, ".mipsdk-*"));
        scenario.Writer.Verify(handler => handler.NotifyCommitSuccessful(scenario.Options.OutputName), Times.Once);
        foreach (var handler in new[] { scenario.Writer, scenario.StagedReader, scenario.FinalReader })
        {
            handler.Verify(value => value.Dispose(), Times.Once);
        }
    }

    [Fact]
    public async Task SameIdStillEvaluatesPolicyAndTrueNoOpCreatesNoOutput()
    {
        using var scenario = new FileScenario();
        scenario.Writer.Setup(handler => handler.IsModified()).Returns(false);
        scenario.Options.GenerateChangeAuditEvent = true;
        var result = await scenario.Action.SetLabelAsync(scenario.Options);
        Assert.False(result.Changed);
        Assert.Null(result.OutputPath);
        scenario.Writer.Verify(handler => handler.SetLabel(scenario.Label, It.IsAny<LabelingOptions>(), It.IsAny<ProtectionSettings>()), Times.Once);
        scenario.Writer.Verify(handler => handler.CommitAsync(It.IsAny<Stream>()), Times.Never);
        scenario.AssertNoOutput();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectionIsVerifiedIndependentlyOfLabelId(bool modified)
    {
        using var scenario = new FileScenario();
        scenario.Label.HasRightsManagementPolicy = true;
        scenario.Writer.Setup(handler => handler.IsModified()).Returns(modified);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
    }

    [Fact]
    public async Task ProtectedOutputRequiresLabelFlagAndAnActualProtectionHandler()
    {
        using var scenario = new FileScenario();
        scenario.Label.HasRightsManagementPolicy = true;
        foreach (var handler in new[] { scenario.StagedReader, scenario.FinalReader })
        {
            handler.SetupGet(value => value.Protection).Returns(Mock.Of<IProtectionHandler>());
        }
        var result = await scenario.Action.SetLabelAsync(scenario.Options);
        Assert.True(result.IsProtected);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidLabelIdsNeverCreateAHandler(string id)
    {
        using var scenario = new FileScenario();
        scenario.Options.LabelId = id;
        await Assert.ThrowsAsync<InputValidationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        Assert.Empty(scenario.ReadPaths);
        scenario.AssertNoOutput();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("group")]
    [InlineData("email")]
    [InlineData("missing")]
    public async Task InapplicableLabelsAreRejected(string kind)
    {
        using var scenario = new FileScenario();
        switch (kind)
        {
            case "inactive": scenario.Label.IsActive = false; break;
            case "group": scenario.Label.Children = new ReadOnlyCollection<Label>([FileScenario.NewLabel()]); break;
            case "email": scenario.Label.ContentFormats = ["email"]; break;
            case "missing": scenario.Engine.Setup(engine => engine.GetLabelById(FileScenario.LabelId)).Returns((Label)null!); break;
        }
        await Assert.ThrowsAsync<InputValidationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
    }

    [Fact]
    public void EnumerationRecursesAndReturnsOnlyActiveFileLeaves()
    {
        using var scenario = new FileScenario();
        var inactive = FileScenario.NewLabel();
        inactive.IsActive = false;
        var email = FileScenario.NewLabel();
        email.ContentFormats = ["email"];
        var group = FileScenario.NewLabel();
        group.Children = new ReadOnlyCollection<Label>([inactive, email, scenario.Label]);
        scenario.Engine.SetupGet(engine => engine.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([group]));
        Assert.Same(scenario.Label, Assert.Single(scenario.Action.ListLabels()));
        scenario.Label.ContentFormats = [];
        Assert.True(SdkAction.IsApplicable(scenario.Label));
        scenario.Label.ContentFormats = null!;
        scenario.Label.Children = null!;
        Assert.True(SdkAction.IsApplicable(scenario.Label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PolicyExceptionsPropagateWithoutRetryCommitOrInputMutation(bool adhoc)
    {
        using var scenario = new FileScenario();
        Exception failure = adhoc ? new AdhocProtectionRequiredException("synthetic")
            : new JustificationRequiredException("synthetic");
        scenario.Writer.Setup(handler => handler.SetLabel(It.IsAny<Label>(), It.IsAny<LabelingOptions>(), It.IsAny<ProtectionSettings>()))
            .Throws(failure);
        var actual = await Record.ExceptionAsync(() => scenario.Action.SetLabelAsync(scenario.Options));
        Assert.Same(failure, actual);
        Assert.Null(scenario.Options.Justification);
        Assert.Null(scenario.Options.AdhocRecipients);
        scenario.Writer.Verify(handler => handler.SetLabel(It.IsAny<Label>(), It.IsAny<LabelingOptions>(), It.IsAny<ProtectionSettings>()), Times.Once);
        scenario.Writer.Verify(handler => handler.CommitAsync(It.IsAny<Stream>()), Times.Never);
        scenario.AssertNoOutput();
    }

    [Fact]
    public async Task ExplicitAuthorizedJustificationIsPassedToSdk()
    {
        using var scenario = new FileScenario();
        scenario.Options.Justification = "Synthetic authorized test reason";
        await scenario.Action.SetLabelAsync(scenario.Options);
        scenario.Writer.Verify(handler => handler.SetLabel(scenario.Label,
            It.Is<LabelingOptions>(options => options.IsDowngradeJustified
                && options.JustificationMessage == "Synthetic authorized test reason"), It.IsAny<ProtectionSettings>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankJustificationIsRejected(string reason)
    {
        using var scenario = new FileScenario();
        scenario.Options.Justification = reason;
        await Assert.ThrowsAsync<InputValidationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        Assert.Empty(scenario.ReadPaths);
    }

    [Fact]
    public async Task AdhocRecipientsMustBeExplicitAndAreNotFabricated()
    {
        using var scenario = new FileScenario();
        scenario.Label.HasAdhocProtection = true;
        scenario.Options.AdhocRecipients = ["synthetic-user@example.invalid"];
        foreach (var handler in new[] { scenario.StagedReader, scenario.FinalReader })
        {
            handler.SetupGet(value => value.Protection).Returns(Mock.Of<IProtectionHandler>());
        }
        await scenario.Action.SetLabelAsync(scenario.Options);
        scenario.Writer.Verify(handler => handler.SetProtection(It.IsAny<ProtectionDescriptor>(), It.IsAny<ProtectionSettings>()), Times.Once);
    }

    [Fact]
    public async Task AdhocRecipientsCannotChangeAnOrdinaryLabelsPermissions()
    {
        using var scenario = new FileScenario();
        scenario.Options.AdhocRecipients = ["synthetic-user@example.invalid"];
        await Assert.ThrowsAsync<InputValidationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("empty")]
    [InlineData("throw")]
    public async Task FailedCommitsAreNotPublished(string kind)
    {
        using var scenario = new FileScenario();
        scenario.Writer.Setup(handler => handler.CommitAsync(It.IsAny<Stream>()))
            .Returns((Stream _) => kind switch
            {
                "throw" => throw new IOException("synthetic"),
                "empty" => Task.FromResult(true),
                _ => Task.FromResult(false)
            });
        await Assert.ThrowsAsync<IOException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrWrongStagedLabelIsNotPublished(bool missing)
    {
        using var scenario = new FileScenario();
        scenario.StagedReader.SetupGet(handler => handler.Label).Returns(missing ? null!
            : new ContentLabel(FileScenario.NewLabel(TestConfig.OtherTenant), DateTime.UtcNow, AssignmentMethod.Standard, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
    }

    [Fact]
    public async Task FinalVerificationFailureRetainsPublishedOutputWithoutSuccessAudit()
    {
        using var scenario = new FileScenario();
        scenario.Options.GenerateChangeAuditEvent = true;
        scenario.FinalReader.SetupGet(handler => handler.Label).Returns((ContentLabel)null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        Assert.True(File.Exists(scenario.Options.OutputName));
        Assert.Equal("synthetic source bytes", File.ReadAllText(scenario.Options.FileName));
        scenario.Writer.Verify(handler => handler.NotifyCommitSuccessful(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentDestinationCreationIsNeverOverwritten()
    {
        using var scenario = new FileScenario();
        scenario.Writer.Setup(handler => handler.CommitAsync(It.IsAny<Stream>()))
            .Returns(async (Stream stream) =>
            {
                await stream.WriteAsync("synthetic output"u8.ToArray());
                File.WriteAllText(scenario.Options.OutputName, "competing destination");
                return true;
            });
        await Assert.ThrowsAsync<IOException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        Assert.Equal("competing destination", File.ReadAllText(scenario.Options.OutputName));
        Assert.Equal("synthetic source bytes", File.ReadAllText(scenario.Options.FileName));
        Assert.Empty(Directory.GetFiles(scenario.Directory.Path, ".mipsdk-*"));
    }

    [Fact]
    public async Task SdkRequiredProtectedExtensionIsRetained()
    {
        using var scenario = new FileScenario();
        scenario.Writer.SetupGet(handler => handler.OutputFileName).Returns("input.pfile");
        await Assert.ThrowsAsync<InputValidationException>(() => scenario.Action.SetLabelAsync(scenario.Options));
        scenario.AssertNoOutput();
        using var other = new FileScenario();
        other.Writer.SetupGet(handler => handler.OutputFileName).Returns("input.pfile");
        other.Options.OutputName = other.Directory.File("output.pfile");
        Assert.True((await other.Action.SetLabelAsync(other.Options)).Changed);
        Assert.Equal(".pfile", Path.GetExtension(other.ReadPaths[1]));
    }

    [Fact]
    public void SynchronousWrapperReturnsChangedStateAndUsesFallbackExtension()
    {
        using var scenario = new FileScenario();
        scenario.Writer.SetupGet(handler => handler.OutputFileName).Returns("");
        Assert.True(scenario.Action.SetLabel(scenario.Options));
    }

    [Fact]
    public void ReadOperationsDisposeHandlersAndSupportUnprotectedContent()
    {
        using var scenario = new FileScenario();
        Assert.Equal(FileScenario.LabelId, scenario.Action.GetLabel(scenario.Options).Label.Id);
        Assert.False(scenario.Action.GetProtectionDetails(scenario.Options).IsProtected);
        scenario.Writer.Verify(handler => handler.Dispose(), Times.Once);
        scenario.StagedReader.Verify(handler => handler.Dispose(), Times.Once);
    }

    [Fact]
    public void ProtectionDetailsReadTheActualProtectionDescriptor()
    {
        using var scenario = new FileScenario();
        var protection = new Mock<IProtectionHandler>();
        protection.SetupGet(handler => handler.ProtectionDescriptor)
            .Returns(new ProtectionDescriptor([new UserRoles(["synthetic-user@example.invalid"], [Roles.Viewer])]));
        scenario.Writer.SetupGet(handler => handler.Protection).Returns(protection.Object);
        var details = scenario.Action.GetProtectionDetails(scenario.Options);
        Assert.True(details.IsProtected);
        Assert.Single(details.UserRoles!);
        Assert.Equal("", details.TemplateId);
        protection.Verify(handler => handler.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DisposalIsOrderedIdempotentAndRejectsFurtherUse()
    {
        var calls = new List<string>();
        var engine = new Mock<IFileEngine>();
        var profile = new Mock<IFileProfile>();
        var context = new Mock<IDisposable>();
        var auth = new Mock<IDisposable>();
        engine.Setup(value => value.Dispose()).Callback(() => calls.Add("engine"));
        profile.Setup(value => value.Dispose()).Callback(() => calls.Add("profile"));
        context.Setup(value => value.Dispose()).Callback(() => calls.Add("context"));
        auth.Setup(value => value.Dispose()).Callback(() => calls.Add("auth"));
        var action = new SdkAction(engine.Object, profile.Object, context.Object, auth.Object);
        action.Dispose();
        action.Dispose();
        Assert.Equal(["engine", "profile", "context", "auth"], calls);
        Assert.Throws<ObjectDisposedException>(() => action.ListLabels());
        Assert.Throws<ObjectDisposedException>(() => action.GetLabel(default));
        Assert.Throws<ObjectDisposedException>(() => action.GetProtectionDetails(default));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => action.SetLabelAsync(default));
    }

    [Fact]
    public void LaterResourcesAreReleasedEvenIfAnEarlierDisposeFails()
    {
        var engine = new Mock<IFileEngine>();
        var profile = new Mock<IFileProfile>();
        var context = new Mock<IDisposable>();
        engine.Setup(value => value.Dispose()).Throws(new IOException("synthetic"));
        using var action = new SdkAction(engine.Object, profile.Object, context.Object);
        Assert.Throws<IOException>(action.Dispose);
        profile.Verify(value => value.Dispose(), Times.Once);
        context.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public void FailedInitializationCleansUpEveryAcquiredResourceWithoutMaskingItsCause()
    {
        var calls = new List<string>();
        var profile = new Mock<IDisposable>();
        var context = new Mock<IDisposable>();
        var auth = new Mock<IDisposable>();
        profile.Setup(value => value.Dispose()).Callback(() => calls.Add("profile")).Throws(new IOException("synthetic cleanup failure"));
        context.Setup(value => value.Dispose()).Callback(() => calls.Add("context"));
        auth.Setup(value => value.Dispose()).Callback(() => calls.Add("auth"));
        SdkAction.CleanupFailedInitialization(profile.Object, null, context.Object, auth.Object);
        Assert.Equal(["profile", "context", "auth"], calls);
    }
}
