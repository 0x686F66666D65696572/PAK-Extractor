using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Index.App.Models;
using Index.Domain.Database.Entities;
using Index.Domain.Database.Repositories;
using Index.Domain.GameProfiles;
using Index.Domain.Models;
using Index.UI.Services;
using Index.UI.ViewModels;
using Prism.Commands;

namespace Index.App.ViewModels
{

  public class LauncherViewModel : WindowViewModel
  {

    #region Data Members

    private readonly IEditorEnvironment _editorEnvironment;
    private readonly IFileDialogService _fileDialogService;
    private readonly IGameProfileManager _profileManager;
    private readonly IGamePathRepository _gamePathRepository;

    private readonly ObservableCollection<LauncherItem> _items;

    #endregion

    #region Properties

    public ObservableCollection<LauncherItem> Items => _items;
    public LauncherItem SelectedItem { get; set; }

    public DelegateCommand ScanPathCommand { get; }
    public DelegateCommand OpenFileCommand { get; }
    public DelegateCommand RemoveSelectedPathCommand { get; }
    public DelegateCommand LaunchCommand { get; }

    #endregion

    #region Constructor

    public LauncherViewModel(
      IFileDialogService fileDialogService,
      IGameProfileManager profileManager,
      IGamePathRepository gamePathRepository,
      IEditorEnvironment editorEnvironment )
      : base( null )
    {
      ASSERT_NOT_NULL( fileDialogService );
      ASSERT_NOT_NULL( profileManager );
      ASSERT_NOT_NULL( gamePathRepository );
      ASSERT_NOT_NULL( editorEnvironment );

      _fileDialogService = fileDialogService;
      _profileManager = profileManager;
      _gamePathRepository = gamePathRepository;
      _editorEnvironment = editorEnvironment;

      _items = new ObservableCollection<LauncherItem>();

      ScanPathCommand = new DelegateCommand( ScanPath );
      OpenFileCommand = new DelegateCommand( OpenFile );
      RemoveSelectedPathCommand = new DelegateCommand( RemoveSelectedGamePath );
      LaunchCommand = new DelegateCommand( LaunchEditor );

      Title = "Select a Game Profile or Open a PAK File";

      ReloadLauncherItems();
    }

    #endregion

    #region Private Methods

    private void LaunchEditor()
    {
      if ( SelectedItem is null )
        return;

      var path = SelectedItem.GamePath;
      if ( SelectedItem.IsSourceFile )
      {
        if ( !File.Exists( path ) )
        {
          ShowWarning( $"The file no longer exists:\n{path}", "File Not Found" );
          return;
        }

        _editorEnvironment.UseSourceFiles( SelectedItem.GameProfile, new[] { path } );
      }
      else
      {
        if ( !Directory.Exists( path ) )
        {
          ShowWarning( $"The directory no longer exists:\n{path}", "Directory Not Found" );
          return;
        }

        _editorEnvironment.UseGamePath( SelectedItem.GameProfile, path );
      }

      CloseAndLaunch();
    }

    /// <summary>
    ///   Opens one or more archive files picked by the user, without needing a game installation path.
    /// </summary>
    internal void OpenFiles( IReadOnlyList<string> filePaths )
    {
      if ( filePaths is null || filePaths.Count == 0 )
        return;

      var profile = _profileManager.ResolveProfileForFiles( filePaths );
      if ( profile is null )
      {
        var supported = string.Join( ", ", _profileManager.GetSupportedFileExtensions() );
        ShowWarning(
          "Could not find a game profile that can open the selected file(s).\n\n" +
          $"Supported file types: {supported}\n\n" +
          "When selecting multiple files, they must all belong to the same game.",
          "Unsupported File" );
        return;
      }

      AddRecentFiles( profile, filePaths );

      _editorEnvironment.UseSourceFiles( profile, filePaths );
      CloseAndLaunch();
    }

    private void OpenFile()
    {
      var filePaths = _fileDialogService.BrowseForOpenFile(
        title: "Select PAK file(s) to open",
        filter: BuildOpenFileFilter(),
        multiselect: true );

      if ( filePaths is null )
        return;

      OpenFiles( filePaths );
    }

    private void CloseAndLaunch()
    {
      Window.DialogResult = true;
      Window.Close();
    }

    private void AddRecentFiles( IGameProfile profile, IEnumerable<string> filePaths )
    {
      var pathsAdded = 0;
      foreach ( var filePath in filePaths.Select( Path.GetFullPath ) )
      {
        if ( _gamePathRepository.CheckIfGamePathAlreadyExists( filePath ) )
          continue;

        _gamePathRepository.Add( new GamePath
        {
          GameId = profile.GameId,
          Path = filePath
        } );
        pathsAdded++;
      }

      if ( pathsAdded > 0 )
        _gamePathRepository.SaveChanges();
    }

    private string BuildOpenFileFilter()
    {
      var filters = new List<string>();

      var allPatterns = _profileManager.GetSupportedFileExtensions().Select( x => $"*{x}" ).ToList();
      if ( allPatterns.Count > 0 )
      {
        var joined = string.Join( ";", allPatterns );
        filters.Add( $"All Supported Archives ({joined})|{joined}" );
      }

      foreach ( var profile in _profileManager.Profiles.Values.OrderBy( x => x.GameName ) )
      {
        if ( profile.SupportedFileExtensions.Count == 0 )
          continue;

        var patterns = string.Join( ";", profile.SupportedFileExtensions.Select( x => $"*{x}" ) );
        filters.Add( $"{profile.GameName} ({patterns})|{patterns}" );
      }

      filters.Add( "All Files (*.*)|*.*" );
      return string.Join( "|", filters );
    }

    private static void ShowWarning( string message, string caption )
      => MessageBox.Show( message, caption, MessageBoxButton.OK, MessageBoxImage.Warning );

    private void ReloadLauncherItems()
    {
      var newItems = new List<LauncherItem>();

      var gamePaths = _gamePathRepository.GetAll();
      foreach ( var gamePath in gamePaths )
      {
        if ( !_profileManager.Profiles.TryGetValue( gamePath.GameId, out var gameProfile ) )
          continue;

        newItems.Add( new LauncherItem
        {
          GameId = gameProfile.GameId,
          GameName = gameProfile.GameName,
          GameIcon = LoadGameIcon( gameProfile ),

          GamePath = gamePath.Path,
          IsSourceFile = IsSourceFilePath( gameProfile, gamePath.Path ),
          GameProfile = gameProfile
        } );
      }

      _items.Clear();
      foreach ( var newItem in newItems )
        _items.Add( newItem );
    }

    private void ScanPath()
    {
      var gamePath = _fileDialogService.BrowseForDirectory( "Select a path to scan" );
      if ( string.IsNullOrWhiteSpace( gamePath ) )
        return;

      var identificationResults = _profileManager.ScanPathForSupportedGames( gamePath );
      if ( !identificationResults.Any() )
        return; // TODO: Show dialog

      var pathsAdded = 0;
      foreach ( var result in identificationResults )
      {
        if ( _gamePathRepository.CheckIfGamePathAlreadyExists( result.GamePath ) )
          continue;

        _gamePathRepository.Add( new GamePath
        {
          GameId = result.GameId,
          Path = result.GamePath
        } );
        pathsAdded++;
      }

      if ( pathsAdded > 0 )
        _gamePathRepository.SaveChanges();

      ReloadLauncherItems();
    }

    private void RemoveSelectedGamePath()
    {
      if ( SelectedItem is null )
        return;

      var gamePath = _gamePathRepository.GetByPath( SelectedItem.GamePath );
      if ( gamePath is null )
        return;

      _gamePathRepository.Delete( gamePath );
      _gamePathRepository.SaveChanges();

      ReloadLauncherItems();
    }

    private static bool IsSourceFilePath( IGameProfile gameProfile, string path )
    {
      var extension = Path.GetExtension( path );
      return !string.IsNullOrEmpty( extension )
        && gameProfile.SupportedFileExtensions.Contains( extension, StringComparer.OrdinalIgnoreCase )
        && !Directory.Exists( path );
    }

    private ImageSource? LoadGameIcon( IGameProfile gameProfile )
    {
      var gameIconStream = gameProfile.LoadGameIcon();
      if ( gameIconStream is null )
        return null;

      var gameIcon = new BitmapImage();
      gameIcon.BeginInit();
      {
        gameIcon.StreamSource = gameIconStream;
        gameIcon.DecodePixelWidth = 64;
      }
      gameIcon.EndInit();

      return gameIcon;
    }

    #endregion

  }

}
