using System.IO;
using System.Linq;
using System.Windows;
using Index.App.ViewModels;
using Index.UI.Windows;

namespace Index.App.Views
{

  public partial class LauncherView : IxWindow
  {

    public LauncherView( LauncherViewModel viewModel )
    {
      DataContext = viewModel;
      InitializeComponent();
    }

    private void OnDragOver( object sender, DragEventArgs e )
    {
      e.Effects = GetDroppedFiles( e ).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
      e.Handled = true;
    }

    private void OnDrop( object sender, DragEventArgs e )
    {
      var files = GetDroppedFiles( e );
      if ( files.Length == 0 )
        return;

      ( ( LauncherViewModel ) DataContext ).OpenFiles( files );
      e.Handled = true;
    }

    private static string[] GetDroppedFiles( DragEventArgs e )
    {
      if ( !e.Data.GetDataPresent( DataFormats.FileDrop ) )
        return System.Array.Empty<string>();

      var paths = e.Data.GetData( DataFormats.FileDrop ) as string[];
      return paths?.Where( File.Exists ).ToArray() ?? System.Array.Empty<string>();
    }

  }

}
