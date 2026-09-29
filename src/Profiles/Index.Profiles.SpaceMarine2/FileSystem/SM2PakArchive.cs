using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using LibSaber.SpaceMarine2.Structures.Resources;

namespace Index.Profiles.SpaceMarine2.FileSystem;

/// <summary>
///   Opens a Space Marine 2 .pak (a ZIP archive).
/// </summary>
/// <remarks>
///   The game keeps an index of each pak in a sibling "&lt;name&gt;.pak.cache" file. When that file is
///   present it is used as upstream does; when it's missing (e.g. a pak copied out of the game folder
///   on its own) the index is built from the ZIP central directory instead.
/// </remarks>
public sealed class SM2PakArchive : IDisposable
{

  #region Constants

  private const uint SIG_LOCAL_HEADER = 0x04034B50;
  private const uint SIG_CENTRAL_HEADER = 0x02014B50;
  private const uint SIG_EOCD = 0x06054B50;
  private const uint SIG_ZIP64_EOCD = 0x06064B50;
  private const uint SIG_ZIP64_LOCATOR = 0x07064B50;

  private const int EOCD_SIZE = 22;
  private const int ZIP64_LOCATOR_SIZE = 20;
  private const int MAX_COMMENT_LENGTH = ushort.MaxValue;

  private const ushort ZIP64_EXTRA_ID = 0x0001;
  private const ushort FLAG_ENCRYPTED = 0x0001;
  private const ushort FLAG_UTF8 = 0x0800;

  #endregion

  #region Data Members

  private readonly fioZIP_FILE? _cachedZip;
  private readonly MemoryMappedFile? _file;
  private readonly MemoryMappedViewAccessor? _accessor;
  private readonly long _fileLength;

  #endregion

  #region Properties

  public IReadOnlyCollection<fioZIP_CACHE_FILE.ENTRY> Entries { get; }

  /// <summary>True when the index was read from the pak itself because no .pak.cache file exists.</summary>
  public bool IsUsingCentralDirectory => _cachedZip is null;

  #endregion

  #region Constructor

  private SM2PakArchive( fioZIP_FILE cachedZip )
  {
    _cachedZip = cachedZip;
    Entries = cachedZip.Entries.Values.ToList();
  }

  private SM2PakArchive( MemoryMappedFile file, MemoryMappedViewAccessor accessor, long fileLength )
  {
    _file = file;
    _accessor = accessor;
    _fileLength = fileLength;
    Entries = ReadCentralDirectory();
  }

  #endregion

  #region Public Methods

  public static SM2PakArchive Open( string filePath )
  {
    if ( File.Exists( filePath + ".cache" ) )
      return new SM2PakArchive( fioZIP_FILE.Open( filePath ) );

    var fileLength = new FileInfo( filePath ).Length;
    if ( fileLength < EOCD_SIZE )
      throw new InvalidDataException( $"File is too small to be a PAK: {filePath}" );

    var stream = new FileStream( filePath, FileMode.Open, FileAccess.Read, FileShare.Read );
    var file = MemoryMappedFile.CreateFromFile( stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false );
    var accessor = file.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );

    try
    {
      return new SM2PakArchive( file, accessor, fileLength );
    }
    catch ( Exception ex )
    {
      accessor.Dispose();
      file.Dispose();
      throw new InvalidDataException(
        $"Could not read {filePath}. No .pak.cache file was found next to it, and the file could not be read as a ZIP archive: {ex.Message}",
        ex );
    }
  }

  public Stream GetFileStream( fioZIP_CACHE_FILE.ENTRY entry )
  {
    if ( _cachedZip is not null )
      return _cachedZip.GetFileStream( entry );

    var zipEntry = ( CentralDirectoryEntry ) entry;
    var dataOffset = zipEntry.ResolveDataOffset( _accessor!, _fileLength );

    switch ( entry.CompressMethod )
    {
      case fioZIP_CACHE_FILE.COMPRESS_METHOD.STORE:
        return _file!.CreateViewStream( dataOffset, entry.CompressedSize, MemoryMappedFileAccess.Read );

      case fioZIP_CACHE_FILE.COMPRESS_METHOD.DEFLATE:
      {
        using var compressed = _file!.CreateViewStream( dataOffset, entry.CompressedSize, MemoryMappedFileAccess.Read );
        using var deflate = new DeflateStream( compressed, CompressionMode.Decompress );

        var output = new MemoryStream( ( int ) Math.Min( entry.Size, int.MaxValue ) );
        deflate.CopyTo( output );
        output.Position = 0;
        return output;
      }

      default:
        throw new NotSupportedException( $"Unsupported compression method {( int ) entry.CompressMethod} for {entry.FileName}." );
    }
  }

  public void Dispose()
  {
    _cachedZip?.Dispose();
    _accessor?.Dispose();
    _file?.Dispose();
  }

  #endregion

  #region Central Directory

  private List<fioZIP_CACHE_FILE.ENTRY> ReadCentralDirectory()
  {
    var accessor = _accessor!;
    var eocdOffset = FindEndOfCentralDirectory();

    long entryCount = accessor.ReadUInt16( eocdOffset + 10 );
    long directoryOffset = accessor.ReadUInt32( eocdOffset + 16 );

    // ZIP64 archives (> 4 GB or > 65535 entries) keep the real values in the ZIP64 end record.
    var locatorOffset = eocdOffset - ZIP64_LOCATOR_SIZE;
    if ( locatorOffset >= 0 && accessor.ReadUInt32( locatorOffset ) == SIG_ZIP64_LOCATOR )
    {
      var zip64EocdOffset = ( long ) accessor.ReadUInt64( locatorOffset + 8 );
      CheckRange( zip64EocdOffset, 56 );
      if ( accessor.ReadUInt32( zip64EocdOffset ) != SIG_ZIP64_EOCD )
        throw new InvalidDataException( "Invalid ZIP64 end of central directory record." );

      entryCount = ( long ) accessor.ReadUInt64( zip64EocdOffset + 32 );
      directoryOffset = ( long ) accessor.ReadUInt64( zip64EocdOffset + 48 );
    }

    var entries = new List<fioZIP_CACHE_FILE.ENTRY>( ( int ) Math.Min( entryCount, 1_000_000 ) );

    var position = directoryOffset;
    for ( long i = 0; i < entryCount; i++ )
    {
      CheckRange( position, 46 );
      if ( accessor.ReadUInt32( position ) != SIG_CENTRAL_HEADER )
        throw new InvalidDataException( $"Invalid central directory header at offset 0x{position:X}." );

      var flags = accessor.ReadUInt16( position + 8 );
      var method = accessor.ReadUInt16( position + 10 );
      long compressedSize = accessor.ReadUInt32( position + 20 );
      long size = accessor.ReadUInt32( position + 24 );
      var nameLength = accessor.ReadUInt16( position + 28 );
      var extraLength = accessor.ReadUInt16( position + 30 );
      var commentLength = accessor.ReadUInt16( position + 32 );
      long localHeaderOffset = accessor.ReadUInt32( position + 42 );

      var namePosition = position + 46;
      CheckRange( namePosition, nameLength + extraLength );
      var name = ReadString( namePosition, nameLength, ( flags & FLAG_UTF8 ) != 0 );

      ApplyZip64Extra( namePosition + nameLength, extraLength, ref size, ref compressedSize, ref localHeaderOffset );

      position = namePosition + nameLength + extraLength + commentLength;

      // Skip directories and anything we can't read.
      if ( name.EndsWith( '/' ) || ( flags & FLAG_ENCRYPTED ) != 0 )
        continue;

      entries.Add( new CentralDirectoryEntry
      {
        FileName = name,
        Size = size,
        CompressedSize = compressedSize,
        CompressMethod = ( fioZIP_CACHE_FILE.COMPRESS_METHOD ) method,
        LocalHeaderOffset = localHeaderOffset
      } );
    }

    return entries;
  }

  private long FindEndOfCentralDirectory()
  {
    var accessor = _accessor!;
    var lowestOffset = Math.Max( 0, _fileLength - EOCD_SIZE - MAX_COMMENT_LENGTH );

    for ( var offset = _fileLength - EOCD_SIZE; offset >= lowestOffset; offset-- )
    {
      if ( accessor.ReadUInt32( offset ) != SIG_EOCD )
        continue;

      // Guard against the signature bytes appearing inside the archive comment.
      var commentLength = accessor.ReadUInt16( offset + 20 );
      if ( offset + EOCD_SIZE + commentLength == _fileLength )
        return offset;
    }

    throw new InvalidDataException( "End of central directory record not found; the file is not a ZIP-based PAK." );
  }

  private void ApplyZip64Extra( long offset, int length, ref long size, ref long compressedSize, ref long localHeaderOffset )
  {
    var accessor = _accessor!;
    var end = offset + length;

    while ( offset + 4 <= end )
    {
      var id = accessor.ReadUInt16( offset );
      var dataLength = accessor.ReadUInt16( offset + 2 );
      var data = offset + 4;

      if ( id == ZIP64_EXTRA_ID )
      {
        // Only the fields that overflowed in the main header are present, in this order.
        var cursor = data;
        if ( size == uint.MaxValue && cursor + 8 <= data + dataLength ) { size = ( long ) accessor.ReadUInt64( cursor ); cursor += 8; }
        if ( compressedSize == uint.MaxValue && cursor + 8 <= data + dataLength ) { compressedSize = ( long ) accessor.ReadUInt64( cursor ); cursor += 8; }
        if ( localHeaderOffset == uint.MaxValue && cursor + 8 <= data + dataLength ) { localHeaderOffset = ( long ) accessor.ReadUInt64( cursor ); }
        return;
      }

      offset = data + dataLength;
    }
  }

  private string ReadString( long offset, int length, bool isUtf8 )
  {
    var bytes = new byte[ length ];
    _accessor!.ReadArray( offset, bytes, 0, length );
    return isUtf8 ? Encoding.UTF8.GetString( bytes ) : Encoding.Latin1.GetString( bytes );
  }

  private void CheckRange( long offset, long length )
  {
    if ( offset < 0 || length < 0 || offset + length > _fileLength )
      throw new InvalidDataException( $"ZIP structure points outside the file (offset 0x{offset:X})." );
  }

  #endregion

  #region Embedded Types

  /// <summary>
  ///   An entry read from the central directory. The data offset lives in each file's local header,
  ///   so it is resolved on first access rather than touching every header while loading.
  /// </summary>
  private sealed class CentralDirectoryEntry : fioZIP_CACHE_FILE.ENTRY
  {
    private bool _isResolved;

    public long LocalHeaderOffset { get; init; }

    public long ResolveDataOffset( MemoryMappedViewAccessor accessor, long fileLength )
    {
      if ( _isResolved )
        return Offset;

      if ( LocalHeaderOffset < 0 || LocalHeaderOffset + 30 > fileLength
        || accessor.ReadUInt32( LocalHeaderOffset ) != SIG_LOCAL_HEADER )
        throw new InvalidDataException( $"Invalid local file header for {FileName}." );

      var nameLength = accessor.ReadUInt16( LocalHeaderOffset + 26 );
      var extraLength = accessor.ReadUInt16( LocalHeaderOffset + 28 );
      var dataOffset = LocalHeaderOffset + 30 + nameLength + extraLength;

      if ( dataOffset + CompressedSize > fileLength )
        throw new InvalidDataException( $"Data for {FileName} extends past the end of the file." );

      Offset = dataOffset;
      _isResolved = true;
      return dataOffset;
    }
  }

  #endregion

}
