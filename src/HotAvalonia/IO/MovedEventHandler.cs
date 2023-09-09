namespace HotAvalonia.IO;

/// <summary>
/// Represents the method that will handle the <see cref="FileWatcher.Moved"/>
/// event of a <see cref="FileWatcher"/> class.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">The <see cref="MovedEventArgs"/> that contains the event data.</param>
internal delegate void MovedEventHandler(object sender, MovedEventArgs e);
