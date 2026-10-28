using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;
using Moq;
using SdkAction = mipsdk.Action;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace mipsdk.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mipsdk-tests-{Guid.NewGuid():N}");

    public TestDirectory() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal static class TestConfig
{
    public const string Client = "11111111-1111-1111-1111-111111111111";
    public const string Tenant = "22222222-2222-2222-2222-222222222222";
    public const string OtherTenant = "44444444-4444-4444-4444-444444444444";

    public static Dictionary<string, string?> Values() => new()
    {
        ["AppSettings:ida:ClientId"] = Client,
        ["AppSettings:ida:TenantGuid"] = Tenant,
        ["AppSettings:ida:IsMultiTenantApp"] = "false",
        ["AppSettings:ida:RedirectUri"] = "http://localhost",
        ["AppSettings:app:Name"] = "Synthetic test application",
        ["AppSettings:app:Version"] = "1.0",
        ["AppSettings:sdk:Cloud"] = "Commercial",
        ["AppSettings:sdk:LogLevel"] = "Warning",
        ["AppSettings:sdk:EnableDiagnosticLogging"] = "false",
        ["AppSettings:sdk:ServiceConsent"] = "AcceptAlways",
        ["AppSettings:sdk:GenerateChangeAuditEvent"] = "false"
    };

    public static AppConfig Create(params (string Key, string? Value)[] overrides)
    {
        var values = Values();
        foreach (var (key, value) in overrides) { values[$"AppSettings:{key}"] = value; }
        return new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}

internal sealed class FileScenario : IDisposable
{
    public const string LabelId = "33333333-3333-3333-3333-333333333333";
    public TestDirectory Directory { get; } = new();
    public Mock<IFileEngine> Engine { get; } = new();
    public Mock<IFileHandler> Writer { get; } = new();
    public Mock<IFileHandler> StagedReader { get; } = new();
    public Mock<IFileHandler> FinalReader { get; } = new();
    public Label Label { get; } = NewLabel();
    public SdkAction Action { get; }
    public SdkAction.FileOptions Options;
    public List<string> ReadPaths { get; } = [];

    public FileScenario()
    {
        Options = new SdkAction.FileOptions
        {
            FileName = Directory.File("input.docx"),
            OutputName = Directory.File("output.docx"),
            LabelId = LabelId,
            AssignmentMethod = AssignmentMethod.Standard
        };
        System.IO.File.WriteAllText(Options.FileName, "synthetic source bytes");
        Engine.Setup(engine => engine.GetLabelById(LabelId)).Returns(Label);
        Engine.SetupGet(engine => engine.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([Label]));
        var handlers = new Queue<IFileHandler>([Writer.Object, StagedReader.Object, FinalReader.Object]);
        Engine.Setup(engine => engine.CreateFileHandlerAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<FileExecutionState>(), It.IsAny<bool>()))
            .Returns((Stream stream, string path, bool _, FileExecutionState _, bool _) =>
            {
                Assert.False(stream.CanWrite);
                ReadPaths.Add(path);
                return Task.FromResult(handlers.Dequeue());
            });
        Writer.Setup(handler => handler.IsModified()).Returns(true);
        Writer.SetupGet(handler => handler.OutputFileName).Returns("input.docx");
        Writer.Setup(handler => handler.CommitAsync(It.IsAny<Stream>()))
            .Returns(async (Stream stream) =>
            {
                await stream.WriteAsync("synthetic labeled bytes"u8.ToArray());
                return true;
            });
        foreach (var handler in new[] { Writer, StagedReader, FinalReader })
        {
            handler.SetupGet(value => value.Label)
                .Returns(() => new ContentLabel(Label, DateTime.UtcNow, AssignmentMethod.Standard, Label.HasRightsManagementPolicy || Label.HasAdhocProtection));
        }
        Action = new SdkAction(Engine.Object);
    }

    public static Label NewLabel(string id = LabelId) => new(id, ActionSource.Manual)
    {
        Name = "Synthetic file label",
        IsActive = true,
        Children = new ReadOnlyCollection<Label>([]),
        ContentFormats = ["file"]
    };

    public void AssertNoOutput()
    {
        Assert.False(System.IO.File.Exists(Options.OutputName));
        Assert.Empty(System.IO.Directory.GetFiles(Directory.Path, ".mipsdk-*"));
        Assert.Equal("synthetic source bytes", System.IO.File.ReadAllText(Options.FileName));
        Writer.Verify(handler => handler.NotifyCommitSuccessful(It.IsAny<string>()), Times.Never);
    }

    public void Dispose()
    {
        Action.Dispose();
        Directory.Dispose();
    }
}
