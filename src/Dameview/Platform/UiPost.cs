namespace Dameview.Platform;

// Schedules an action for execution by the application's UI thread.
internal delegate void UiPost(Action action);
