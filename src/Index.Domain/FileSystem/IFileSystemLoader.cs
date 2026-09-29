namespace Index.Domain.FileSystem
{

  public interface IFileSystemLoader
  {

    event Action<double> ProgressChanged;

    void SetBasePath( string basePath );
    void SetSourceFiles( IEnumerable<string> filePaths );
    Task<IReadOnlyList<IFileSystemDevice>> LoadDevices();

  }

}
