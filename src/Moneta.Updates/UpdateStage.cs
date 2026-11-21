namespace Moneta.Updates;

public sealed class UpdateStage(Guid id)
{
    public string Id => id.ToString("N");
    public string Directory => Path.Combine(UpdateChannel.StagingRoot, Id);
    public string Installer => Path.Combine(Directory, "setup.exe");
    public string Envelope => Path.Combine(Directory, UpdateManifest.EnvelopeName);
    public string Helper => Path.Combine(Directory, "Moneta.UpdateHost.exe");
    public string Ready => Path.Combine(Directory, "ready");
    public string Result => Path.Combine(Directory, "result");

    public void Clean()
    {
        try
        {
            if (File.GetAttributes(Directory).HasFlag(FileAttributes.ReparsePoint)) return;
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        foreach (var path in new[] { Installer, Envelope, Helper, Ready, Result })
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { System.IO.Directory.Delete(Directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
