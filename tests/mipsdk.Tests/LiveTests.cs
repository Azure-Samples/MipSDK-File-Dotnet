using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using SdkAction = mipsdk.Action;

namespace mipsdk.Tests;

public sealed class LiveTests
{
    [LiveFact("MIPSDK_LIVE_LABEL_ID")]
    [Trait("Category", "Live")]
    public Task LabelAndRepeatGeneratedDocx() => ExerciseAsync("MIPSDK_LIVE_LABEL_ID", requireProtection: false);

    [LiveFact("MIPSDK_LIVE_PROTECTED_LABEL_ID")]
    [Trait("Category", "Live")]
    public Task ProtectAndRepeatGeneratedDocx() => ExerciseAsync("MIPSDK_LIVE_PROTECTED_LABEL_ID", requireProtection: true);

    private static async Task ExerciseAsync(string setting, bool requireProtection)
    {
        using var directory = new TestDirectory();
        var source = directory.File("generated.docx");
        CreateDocx(source);
        var original = SHA256.HashData(await File.ReadAllBytesAsync(source));
        using var config = new AppConfig();
        using var action = await SdkAction.CreateAsync(config,
            Environment.GetEnvironmentVariable("MIPSDK_LIVE_USER")!);
        var options = new SdkAction.FileOptions
        {
            FileName = source,
            OutputName = directory.File("labeled.docx"),
            LabelId = Environment.GetEnvironmentVariable(setting)!,
            AssignmentMethod = Microsoft.InformationProtection.AssignmentMethod.Standard
        };
        var first = await action.SetLabelAsync(options);
        Assert.True(first.Changed);
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        options.FileName = first.OutputPath!;
        var label = action.GetLabel(options);
        Assert.Equal(options.LabelId, label.Label.Id, ignoreCase: true);
        if (requireProtection)
        {
            Assert.True(first.IsProtected);
            Assert.True(label.IsProtectionAppliedFromLabel);
            Assert.True(action.GetProtectionDetails(options).IsProtected);
        }

        var beforeRepeat = SHA256.HashData(await File.ReadAllBytesAsync(options.FileName));
        options.OutputName = directory.File("repeated.docx");
        var repeated = await action.SetLabelAsync(options);
        Assert.Equal(beforeRepeat, SHA256.HashData(await File.ReadAllBytesAsync(options.FileName)));
        Assert.Equal(repeated.Changed, File.Exists(options.OutputName));
        if (requireProtection) { Assert.True(repeated.IsProtected); }
    }

    private static void CreateDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("[Content_Types].xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        Write("_rels/.rels", """
            <?xml version="1.0" encoding="utf-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        Write("word/document.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Synthetic throwaway MIP SDK integration test.</w:t></w:r></w:p></w:body></w:document>
            """);
        void Write(string name, string contents)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(contents);
        }
    }
}

internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string labelSetting, [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable("MIPSDK_RUN_LIVE") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MIPSDK_LIVE_USER"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(labelSetting)))
        {
            Skip = $"Requires explicit MIPSDK_RUN_LIVE=1, MIPSDK_LIVE_USER, and an approved {labelSetting}.";
        }
    }
}
