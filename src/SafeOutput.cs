namespace mipsdk;

internal sealed class SafeOutput : IDisposable
{
    private readonly string outputPath;
    private bool published;
    public string StagePath { get; }
    public FileStream Stream { get; }

    public SafeOutput(string outputPath, string extension)
    {
        this.outputPath = outputPath;
        StagePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".mipsdk-{Guid.NewGuid():N}{extension}");
        Stream = new FileStream(StagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public static (string Input, string Output) ValidatePaths(string input, string output)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            throw new InputValidationException("Input and output file paths are required.");
        }
        var source = Path.GetFullPath(input);
        var destination = Path.GetFullPath(output);
        if (!File.Exists(source))
        {
            throw new InputValidationException("The input file does not exist.");
        }
        if (source.Equals(destination, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || File.Exists(destination) || Directory.Exists(destination))
        {
            throw new InputValidationException("Choose a new output file. In-place changes and overwriting are not supported.");
        }
        if (!Directory.Exists(Path.GetDirectoryName(destination)))
        {
            throw new InputValidationException("The output directory must already exist.");
        }
        return (source, destination);
    }

    public async Task FinishWritingAsync()
    {
        await Stream.FlushAsync().ConfigureAwait(false);
        if (Stream.Length == 0)
        {
            throw new IOException("The SDK produced an empty output.");
        }
        await Stream.DisposeAsync().ConfigureAwait(false);
    }

    public void Publish()
    {
        File.Move(StagePath, outputPath, overwrite: false);
        published = true;
    }

    public void Dispose()
    {
        Stream.Dispose();
        if (!published)
        {
            try { File.Delete(StagePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
